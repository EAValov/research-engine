using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.JSInterop;
using ResearchEngine.Blazor.Api;

namespace ResearchEngine.Blazor.Services;

/// <summary>
/// Browser EventSource-based job events streaming:
/// - fetches an SSE ticket via POST /jobs/{jobId}/events/stream-token (authorized)
/// - opens EventSource(StreamUrl) (anonymous, ticket query)
/// - reconnects with exponential backoff until "done"
/// - dedupes by event Id on the client side
/// </summary>
public sealed class JobEventsClient : IAsyncDisposable
{
    private readonly IResearchApiClient _api;
    private readonly IJSRuntime _js;
    private readonly HttpClient _http; 

    private IJSObjectReference? _module;
    private string? _connId;
    private DotNetObjectReference<SseCallbackHub>? _hubRef;

    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentDictionary<int, byte> _seenEventIds = new();
    private volatile bool _doneReceived;

    public JobEventsClient(IResearchApiClient api, IJSRuntime js, HttpClient http)
    {
        _api = api;
        _js = js;
        _http = http;
    }

    public sealed record PersistedJobEvent(int Id, DateTimeOffset Timestamp, string Stage, string Message);
    public sealed record DonePayload(Guid JobId, string Status, Guid? SynthesisId);

    public enum JobStreamKind { Event, Done }

    public sealed record JobStreamItem(JobStreamKind Kind, PersistedJobEvent? Event, DonePayload? Done)
    {
        public static JobStreamItem FromEvent(PersistedJobEvent e) => new(JobStreamKind.Event, e, null);
        public static JobStreamItem FromDone(DonePayload d) => new(JobStreamKind.Done, null, d);
    }

    public async Task StartAsync(
        Guid jobId,
        Func<JobStreamItem, Task> onItem,
        Func<ApiError, Task> onError,
        CancellationToken ct)
    {
        await EnsureModuleAsync(ct);

        _doneReceived = false;
        _seenEventIds.Clear();

        _ = Task.Run(() => RunLoopAsync(jobId, onItem, onError, ct), ct);
    }

    public async Task StopAsync()
    {
        _doneReceived = true;
        await CloseCurrentAsync();
    }

    private async Task RunLoopAsync(
        Guid jobId,
        Func<JobStreamItem, Task> onItem,
        Func<ApiError, Task> onError,
        CancellationToken ct)
    {
        var attempt = 0;

        while (!ct.IsCancellationRequested && !_doneReceived)
        {
            attempt++;

            // 1) Get token (authorized)
            CreateSseTokenResponse? token;
            try
            {
                token = await _api.StreamTokenAsync(jobId, ct);
            }
            catch (Exception ex)
            {
                var err = ApiErrorMapper.Map(ex);
                await SafeInvoke(onError, err);

                var delayMs = BackoffMs(attempt, capMs: 15000);
                await DelaySafe(delayMs, ct);
                continue;
            }

            // 2) Connect EventSource(absoluteUrl) (anonymous)
            var tcsDisconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            await CloseCurrentAsync();

            _hubRef = DotNetObjectReference.Create(new SseCallbackHub(
                onEventJson: async json =>
                {
                    try
                    {
                        var dto = JsonSerializer.Deserialize<PersistedJobEventDto>(json, _json);
                        if (dto is null) return;

                        if (!_seenEventIds.TryAdd(dto.Id, 0))
                            return; // dedupe

                        var ev = new PersistedJobEvent(dto.Id, dto.Timestamp, dto.Stage ?? "", dto.Message ?? "");
                        await SafeInvoke(onItem, JobStreamItem.FromEvent(ev));
                    }
                    catch
                    {
                        // ignore malformed event payloads
                    }
                },
                onDoneJson: async json =>
                {
                    try
                    {
                        var dto = JsonSerializer.Deserialize<DonePayloadDto>(json, _json);
                        if (dto is null) return;

                        _doneReceived = true;

                        Guid? synthesisId = null;
                        if (Guid.TryParse(dto.SynthesisId, out var parsed))
                            synthesisId = parsed;

                        var done = new DonePayload(
                            JobId: Guid.TryParse(dto.JobId, out var jid) ? jid : jobId,
                            Status: dto.Status ?? "",
                            SynthesisId: synthesisId);

                        await SafeInvoke(onItem, JobStreamItem.FromDone(done));
                    }
                    finally
                    {
                        tcsDisconnected.TrySetResult();
                    }
                },
                onError: async () =>
                {
                    tcsDisconnected.TrySetResult();
                    await Task.CompletedTask;
                }));

            try
            {
                var url = token.StreamUrl ?? "";
                if (string.IsNullOrWhiteSpace(url))
                    throw new InvalidOperationException("StreamUrl is empty.");

                var absoluteUrl = ToAbsoluteApiUrl(_http.BaseAddress, url);

                _connId = await _module!.InvokeAsync<string>("connect", ct, absoluteUrl, _hubRef);

                await Task.WhenAny(tcsDisconnected.Task, WaitCancellation(ct));

                await CloseCurrentAsync();

                if (_doneReceived || ct.IsCancellationRequested)
                    break;

                var backoffMs = BackoffMs(attempt, capMs: 15000);
                await DelaySafe(backoffMs, ct);
            }
            catch (Exception ex)
            {
                await CloseCurrentAsync();

                if (ct.IsCancellationRequested || _doneReceived)
                    break;

                var err = ApiErrorMapper.Map(ex);
                await SafeInvoke(onError, err);

                var backoffMs = BackoffMs(attempt, capMs: 15000);
                await DelaySafe(backoffMs, ct);
            }
        }
    }

    private static string ToAbsoluteApiUrl(Uri? apiBase, string streamUrl)
    {
        if (apiBase is null)
            throw new InvalidOperationException("HttpClient.BaseAddress is null; cannot resolve StreamUrl.");

        // Important: use API origin (scheme+host+port) even if BaseAddress has a path
        var origin = new Uri(apiBase.GetLeftPart(UriPartial.Authority));

        // streamUrl is "/api/....?ticket=..." (relative)
        // DO NOT encode; EventSource must receive a normal URL with "?"
        if (streamUrl.StartsWith("/", StringComparison.Ordinal))
            return new Uri(origin, streamUrl).ToString();

        return new Uri(origin, "/" + streamUrl).ToString();
    }

    private async Task CloseCurrentAsync()
    {
        try
        {
            if (_connId is not null)
                await _module!.InvokeVoidAsync("close", _connId);
        }
        catch
        {
            // ignore
        }
        finally
        {
            _connId = null;

            try { _hubRef?.Dispose(); } catch { }
            _hubRef = null;
        }
    }

    private async Task EnsureModuleAsync(CancellationToken ct)
    {
        // ✅ Correct module path under wwwroot/js/
        _module ??= await _js.InvokeAsync<IJSObjectReference>("import", ct, "./js/jobEvents.js");
    }

    private static int BackoffMs(int attempt, int capMs)
    {
        var ms = 250 * (int)Math.Pow(2, Math.Min(attempt - 1, 6));
        return Math.Min(ms, capMs);
    }

    private static Task WaitCancellation(CancellationToken ct)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ct.Register(static s => ((TaskCompletionSource)s!).TrySetResult(), tcs);
        return tcs.Task;
    }

    private static async Task DelaySafe(int ms, CancellationToken ct)
    {
        try { await Task.Delay(ms, ct); } catch { }
    }

    private static async Task SafeInvoke<T>(Func<T, Task> cb, T arg)
    {
        try { await cb(arg); } catch { }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();

        try
        {
            if (_module is not null)
                await _module.DisposeAsync();
        }
        catch { }
    }

    private sealed record PersistedJobEventDto(int Id, DateTimeOffset Timestamp, string? Stage, string? Message);
    private sealed record DonePayloadDto(string? JobId, string? Status, string? SynthesisId);

    private sealed class SseCallbackHub
    {
        private readonly Func<string, Task> _onEventJson;
        private readonly Func<string, Task> _onDoneJson;
        private readonly Func<Task> _onError;

        public SseCallbackHub(Func<string, Task> onEventJson, Func<string, Task> onDoneJson, Func<Task> onError)
        {
            _onEventJson = onEventJson;
            _onDoneJson = onDoneJson;
            _onError = onError;
        }

        [JSInvokable] public Task OnSseEvent(string json) => _onEventJson(json);
        [JSInvokable] public Task OnSseDone(string json) => _onDoneJson(json);
        [JSInvokable] public Task OnSseError() => _onError();
    }
}

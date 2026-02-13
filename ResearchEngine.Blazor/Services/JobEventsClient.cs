using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.JSInterop;
using ResearchEngine.Blazor.Api;

namespace ResearchEngine.Blazor.Services;

/// <summary>
/// Browser EventSource-based job events streaming:
/// - POST /jobs/{jobId}/events/stream-token (authorized) -> ticket + streamUrl
/// - EventSource(streamUrl absolute) (anonymous; ticket is querystring)
/// - reconnects with exponential backoff until "done"
/// - dedupes by event Id on the client side (internal seen set)
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

    private CancellationTokenSource? _runCts;
    private Task? _runTask;

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

    /// <summary>
    /// Preferred overload:
    /// - JobDetail loads persisted events via GET /events and renders them
    /// - JobEventsClient dedupes all streamed events internally (and can optionally be seeded via the other overload)
    /// </summary>
    public Task StartAsync(
        Guid jobId,
        Func<JobStreamItem, Task> onItem,
        Func<ApiError, Task> onError,
        CancellationToken ct)
        => StartCoreAsync(jobId, alreadySeenEventIds: null, shouldReconnect: null, onItem, onError, ct);

    /// <summary>
    /// Backward-compatible overload (seed dedupe + external reconnect guard).
    /// If you still call this from some page/component, it will keep working.
    /// </summary>
    public Task StartAsync(
        Guid jobId,
        IEnumerable<int>? alreadySeenEventIds,
        Func<bool>? shouldReconnect,
        Func<JobStreamItem, Task> onItem,
        Func<ApiError, Task> onError,
        CancellationToken ct)
        => StartCoreAsync(jobId, alreadySeenEventIds, shouldReconnect, onItem, onError, ct);

    private async Task StartCoreAsync(
        Guid jobId,
        IEnumerable<int>? alreadySeenEventIds,
        Func<bool>? shouldReconnect,
        Func<JobStreamItem, Task> onItem,
        Func<ApiError, Task> onError,
        CancellationToken ct)
    {
        await EnsureModuleAsync(ct);

        // Stop any previous run
        await StopAsync();

        _doneReceived = false;
        _seenEventIds.Clear();

        if (alreadySeenEventIds is not null)
        {
            foreach (var id in alreadySeenEventIds)
                _seenEventIds.TryAdd(id, 0);
        }

        _runCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var runToken = _runCts.Token;

        _runTask = Task.Run(() => RunLoopAsync(jobId, shouldReconnect, onItem, onError, runToken), runToken);
    }

    public async Task StopAsync()
    {
        _doneReceived = true;

        try { _runCts?.Cancel(); } catch { }
        _runCts?.Dispose();
        _runCts = null;

        await CloseCurrentAsync();

        try
        {
            if (_runTask is not null)
                await _runTask;
        }
        catch
        {
            // ignore
        }
        finally
        {
            _runTask = null;
        }
    }

    private async Task RunLoopAsync(
        Guid jobId,
        Func<bool>? shouldReconnect,
        Func<JobStreamItem, Task> onItem,
        Func<ApiError, Task> onError,
        CancellationToken ct)
    {
        var attempt = 0;

        while (!ct.IsCancellationRequested && !_doneReceived)
        {
            if (shouldReconnect is not null && !shouldReconnect())
                break;

            attempt++;

            // 1) Get token (authorized)
            CreateSseTokenResponse token;
            try
            {
                // NOTE: method name depends on NSwag. In your current file it is StreamTokenAsync.
                token = await _api.StreamTokenAsync(jobId, ct);
            }
            catch (Exception ex)
            {
                await SafeInvoke(onError, ApiErrorMapper.Map(ex));
                await DelaySafe(BackoffMs(attempt, 15000), ct);
                continue;
            }

            // 2) Connect EventSource (anonymous stream)
            var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

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

                        var ev = new PersistedJobEvent(
                            dto.Id,
                            dto.Timestamp,
                            dto.Stage ?? string.Empty,
                            dto.Message ?? string.Empty);

                        await SafeInvoke(onItem, JobStreamItem.FromEvent(ev));
                    }
                    catch
                    {
                        // ignore malformed payloads
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
                        if (!string.IsNullOrWhiteSpace(dto.SynthesisId) && Guid.TryParse(dto.SynthesisId, out var sid))
                            synthesisId = sid;

                        var jid = jobId;
                        if (!string.IsNullOrWhiteSpace(dto.JobId) && Guid.TryParse(dto.JobId, out var parsedJid))
                            jid = parsedJid;

                        var done = new DonePayload(
                            JobId: jid,
                            Status: dto.Status ?? string.Empty,
                            SynthesisId: synthesisId);

                        await SafeInvoke(onItem, JobStreamItem.FromDone(done));
                    }
                    finally
                    {
                        disconnected.TrySetResult();
                    }
                },
                onError: async () =>
                {
                    disconnected.TrySetResult();
                    await Task.CompletedTask;
                }));

            try
            {
                var url = token.StreamUrl ?? string.Empty;
                if (string.IsNullOrWhiteSpace(url))
                    throw new InvalidOperationException("StreamUrl is empty.");

                // IMPORTANT: token.StreamUrl is usually "/api/research/...."
                // Ensure we always connect to the API origin (http://localhost:8090),
                // not the SPA origin (http://localhost:5173) and not "file:///".
                var absoluteUrl = ToAbsoluteApiUrl(_http.BaseAddress, url);

                _connId = await _module!.InvokeAsync<string>("connect", ct, absoluteUrl, _hubRef);

                // after a successful connect, reset backoff
                attempt = 0;

                // Wait for disconnect or cancellation
                await Task.WhenAny(disconnected.Task, WaitCancellation(ct));

                await CloseCurrentAsync();

                if (_doneReceived || ct.IsCancellationRequested)
                    break;

                if (shouldReconnect is not null && !shouldReconnect())
                    break;

                await DelaySafe(BackoffMs(1, 15000), ct); // short pause after disconnect
            }
            catch (Exception ex)
            {
                await CloseCurrentAsync();

                if (_doneReceived || ct.IsCancellationRequested)
                    break;

                await SafeInvoke(onError, ApiErrorMapper.Map(ex));
                await DelaySafe(BackoffMs(attempt, 15000), ct);
            }
        }
    }

    private static string ToAbsoluteApiUrl(Uri? apiBase, string streamUrl)
    {
        if (apiBase is null)
            throw new InvalidOperationException("HttpClient.BaseAddress is null; cannot resolve StreamUrl.");

        // Use API origin (scheme+host+port), ignore any base path
        var origin = new Uri(apiBase.GetLeftPart(UriPartial.Authority));

        // streamUrl is typically "/api/research/jobs/.../events/stream?ticket=..."
        if (streamUrl.StartsWith("/", StringComparison.Ordinal))
            return new Uri(origin, streamUrl).ToString();

        return new Uri(origin, "/" + streamUrl).ToString();
    }

    private async Task CloseCurrentAsync()
    {
        try
        {
            if (_connId is not null && _module is not null)
                await _module.InvokeVoidAsync("close", _connId);
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
        _module ??= await _js.InvokeAsync<IJSObjectReference>("import", ct, "./js/jobEvents.js");
    }

    private static int BackoffMs(int attempt, int capMs)
    {
        // 250ms, 500ms, 1s, 2s, 4s, 8s, 16s... capped
        var exp = Math.Min(Math.Max(attempt - 1, 0), 6);
        var ms = 250 * (int)Math.Pow(2, exp);
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
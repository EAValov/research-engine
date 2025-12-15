using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using ResearchApi.Domain;

namespace ResearchApi.Infrastructure;

public class WebhookDispatcher : IWebhookDispatcher, IHostedService
{
    private readonly IWebhookSubscriptionStore _subscriptionStore;
    private readonly ILogger<WebhookDispatcher> _logger;
    private readonly HttpClient _httpClient;
    private readonly Channel<WebhookDeliveryRequest> _channel;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private Task? _processingTask;

    public WebhookDispatcher(
        IWebhookSubscriptionStore subscriptionStore,
        ILogger<WebhookDispatcher> logger,
        HttpClient httpClient)
    {
        _subscriptionStore = subscriptionStore;
        _logger = logger;
        _httpClient = httpClient;
        _channel = Channel.CreateBounded<WebhookDeliveryRequest>(new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.Wait
        });
        _cancellationTokenSource = new CancellationTokenSource();
    }

    public async Task EnqueueAsync(WebhookDeliveryRequest request, CancellationToken ct)
    {
        try
        {
            await _channel.Writer.WriteAsync(request, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enqueue webhook delivery for job {JobId}", request.JobId);
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _processingTask = ProcessQueueAsync(_cancellationTokenSource.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping webhook dispatcher");
        
        _cancellationTokenSource.Cancel();
        if (_processingTask != null)
        {
            await _processingTask;
        }
        
        _channel.Writer.Complete();
        _cancellationTokenSource.Dispose();
        _logger.LogInformation("Webhook dispatcher stopped");
    }

    private async Task ProcessQueueAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var request in _channel.Reader.ReadAllAsync(cancellationToken))
            {
                await ProcessDeliveryAsync(request, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Webhook dispatcher processing cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in webhook dispatcher");
        }
    }

    private async Task ProcessDeliveryAsync(WebhookDeliveryRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var subscription = await _subscriptionStore.GetAsync(request.JobId, cancellationToken);
            if (subscription == null)
            {
                _logger.LogDebug("No webhook subscription found for job {JobId}", request.JobId);
                return;
            }

            // Check if the stage is included in the subscription
            if (!subscription.Stages.Contains(request.Stage))
            {
                _logger.LogDebug("Stage {Stage} not included in subscription for job {JobId}", request.Stage, request.JobId);
                return;
            }

            await SendWebhookAsync(subscription, request, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process webhook delivery for job {JobId}", request.JobId);
        }
    }

    private async Task SendWebhookAsync(WebhookSubscription subscription, WebhookDeliveryRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var payload = new WebhookPayload(request.JobId, request.Stage, request.TimestampUtc, request.Data);
            
            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            // Add headers
            var httpRequest = new HttpRequestMessage(HttpMethod.Post, subscription.Url)
            {
                Content = content
            };
            
            httpRequest.Headers.Add("X-DeepResearch-Stage", request.Stage.ToString());
            httpRequest.Headers.Add("X-DeepResearch-JobId", request.JobId.ToString());

            // Add signature if secret exists
            if (!string.IsNullOrEmpty(subscription.Secret))
            {
                var signature = ComputeSignature(subscription.Secret, json);
                httpRequest.Headers.Add("X-DeepResearch-Signature", $"sha256={signature}");
            }

            _logger.LogDebug("Sending webhook for job {JobId} to {Url}", request.JobId, subscription.Url);

            var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Webhook delivery failed with status code {StatusCode} for job {JobId}", 
                    response.StatusCode, request.JobId);
            }
            else
            {
                _logger.LogDebug("Webhook delivered successfully for job {JobId}", request.JobId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send webhook for job {JobId}", request.JobId);
        }
    }

    private static string ComputeSignature(string secret, string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

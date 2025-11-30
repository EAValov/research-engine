using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Logging;

namespace ResearchApi.Infrastructure;

public class HttpFileLoggingHandler : DelegatingHandler
{
    private readonly ILogger<HttpFileLoggingHandler> _logger;

    public HttpFileLoggingHandler(ILogger<HttpFileLoggingHandler> logger)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        string? body = null;
        if (request.Content != null)
        {
            body = await request.Content.ReadAsStringAsync(cancellationToken);
        }

        var httpSnippet = BuildHttpFileSnippet(request, body);

        _logger.LogDebug("Outgoing HTTP request (.http format):\n{HttpRequest}", httpSnippet);

        var response = await base.SendAsync(request, cancellationToken);

        // Optional: log response too (not .http, just info)
        var responseBody = response.Content != null
            ? await response.Content.ReadAsStringAsync(cancellationToken)
            : null;

        _logger.LogDebug(
            "HTTP response {StatusCode} from {Url}\nHeaders: {Headers}\nBody: {Body}",
            (int)response.StatusCode,
            request.RequestUri,
            response.Headers,
            responseBody
        );

        return response;
    }

    private static string BuildHttpFileSnippet(HttpRequestMessage request, string? body)
    {
        var sb = new StringBuilder();

        // Separator so you can paste many requests into one .http file
        sb.AppendLine("### Logged request");
        
        // First line: METHOD URL (HTTP version optional, most clients don’t require it)
        sb.AppendLine($"{request.Method} {request.RequestUri}");

        // Headers from request
        foreach (var header in request.Headers)
        {
            foreach (var value in header.Value)
            {
                sb.AppendLine($"{header.Key}: {MaskIfSensitive(header.Key, value)}");
            }
        }

        // Headers from content
        if (request.Content != null)
        {
            foreach (var header in request.Content.Headers)
            {
                foreach (var value in header.Value)
                {
                    sb.AppendLine($"{header.Key}: {value}");
                }
            }
        }

        // Body
        if (!string.IsNullOrEmpty(body))
        {
            sb.AppendLine();
            sb.AppendLine(body);
        }

        return sb.ToString();
    }

    private static string MaskIfSensitive(string headerName, string value)
    {
        if (headerName.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
        {
            return "***";
        }

        return value;
    }
}
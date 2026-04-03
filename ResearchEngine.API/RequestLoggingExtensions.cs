using Serilog;
using Serilog.Context;
using Serilog.Events;

namespace ResearchEngine.API;

public static class RequestLoggingExtensions
{
    public static WebApplication UseResearchEngineRequestLogging(this WebApplication app)
    {
        var isTesting = app.Environment.IsEnvironment("Testing");

        app.Use(async (context, next) =>
        {
            var correlationId = ResolveCorrelationId(context);
            var requestPath = context.Request.Path.HasValue ? context.Request.Path.Value! : "/";
            var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            context.Response.Headers["X-Correlation-ID"] = correlationId;

            using (LogContext.PushProperty("CorrelationId", correlationId))
            using (LogContext.PushProperty("RequestId", context.TraceIdentifier))
            using (LogContext.PushProperty("RequestMethod", context.Request.Method))
            using (LogContext.PushProperty("RequestPath", requestPath))
            using (LogContext.PushProperty("ClientIp", clientIp))
            {
                await next();
            }
        });

        app.UseSerilogRequestLogging(options =>
        {
            options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
            options.GetLevel = (httpContext, _, ex) =>
            {
                if (ex is not null || httpContext.Response.StatusCode >= StatusCodes.Status500InternalServerError)
                    return LogEventLevel.Error;

                if (isTesting)
                    return LogEventLevel.Debug;

                if (httpContext.Response.StatusCode >= StatusCodes.Status400BadRequest)
                    return LogEventLevel.Warning;

                var path = httpContext.Request.Path.Value ?? string.Empty;
                if (path.StartsWith("/health", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith("/events/stream", StringComparison.OrdinalIgnoreCase))
                {
                    return LogEventLevel.Debug;
                }

                return LogEventLevel.Information;
            };
            options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
            {
                diagnosticContext.Set("CorrelationId", ResolveCorrelationId(httpContext));
                diagnosticContext.Set("RequestId", httpContext.TraceIdentifier);
                diagnosticContext.Set("ClientIp", httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");

                var endpointName = httpContext.GetEndpoint()?.DisplayName;
                if (!string.IsNullOrWhiteSpace(endpointName))
                    diagnosticContext.Set("EndpointName", endpointName);

                AddRouteValue(diagnosticContext, httpContext, "jobId", "JobId");
                AddRouteValue(diagnosticContext, httpContext, "synthesisId", "SynthesisId");
                AddRouteValue(diagnosticContext, httpContext, "sourceId", "SourceId");
                AddRouteValue(diagnosticContext, httpContext, "learningId", "LearningId");
            };
        });

        return app;
    }

    private static string ResolveCorrelationId(HttpContext context)
    {
        var correlationId = context.Request.Headers["X-Correlation-ID"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(correlationId))
            correlationId = context.Request.Headers["X-Request-ID"].FirstOrDefault();

        return string.IsNullOrWhiteSpace(correlationId)
            ? context.TraceIdentifier
            : correlationId;
    }

    private static void AddRouteValue(IDiagnosticContext diagnosticContext, HttpContext httpContext, string routeKey, string propertyName)
    {
        if (httpContext.Request.RouteValues.TryGetValue(routeKey, out var value) &&
            value is not null &&
            !string.IsNullOrWhiteSpace(value.ToString()))
        {
            diagnosticContext.Set(propertyName, value);
        }
    }
}

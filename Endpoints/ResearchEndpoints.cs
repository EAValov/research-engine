using Microsoft.AspNetCore.Mvc;
using ResearchApi.Domain;
using ResearchApi.Prompts;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ResearchApi.Configuration;

namespace ResearchApi.Endpoints;

public static class ResearchEndpoints
{
    public static void MapResearchEndpoints(this WebApplication app)
    {
        app.MapPost("/api/research/plan", async (
            [FromBody] PlanRequest request,
            [FromServices] IResearchOrchestrator orchestrator,
            CancellationToken ct) =>
        {
            var serp_questions =  await orchestrator.GenerateFeedbackQueries(request.Query, request.MaxQuestions, false, ct);
            
            return Results.Ok(new PlanResponse(request.Query, serp_questions));
        });

        app.MapPost("/api/research/run", async (
            [FromBody] RunRequest request,
            [FromServices] IResearchOrchestrator orchestrator) =>
        {
            var clarifications = request.Answers
                .Where(a => !string.IsNullOrWhiteSpace(a.Answer))
                .Select(a => new Clarification(a.Question, a.Answer))
                .ToList();

            // Create a new research job with clarifications
            var job = orchestrator.StartJob(
                request.Query,
                clarifications,
                request.Breadth,
                request.Depth);

            // Start processing in the background (fire-and-forget)
            _ = Task.Run(async () =>
            {
                try
                {
                    await orchestrator.RunJobAsync(job.Id, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    // TODO: replace with real logging
                    Console.WriteLine($"Error running research job {job.Id}: {ex}");
                }
            });

            return Results.Ok(new RunResponse(job.Id, job.Status.ToString()));
        });

        app.MapGet("/api/research/stream/{jobId}", async (
            Guid jobId,
            HttpContext httpContext,
            [FromServices] IResearchJobStore jobStore,
            CancellationToken ct) =>
        {
            var response = httpContext.Response;
            response.Headers.CacheControl = "no-cache";
            response.Headers.Connection = "keep-alive";
            response.Headers["X-Accel-Buffering"] = "no"; // for some proxies (nginx)
            response.ContentType = "text/event-stream";

            var lastIndex = 0;
            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            // Helper to write one SSE event
            async Task WriteSseEventAsync(string eventName, object data)
            {
                var json = JsonSerializer.Serialize(data, jsonOptions);
                await response.WriteAsync($"event: {eventName}\n", ct);
                await response.WriteAsync($"data: {json}\n\n", ct);
                await response.Body.FlushAsync(ct);
            }

            // Initial check
            var job = jobStore.GetJob(jobId);
            if (job is null)
            {
                await WriteSseEventAsync("error", new { message = "Job not found" });
                return;
            }

            // Optional: send initial status
            await WriteSseEventAsync("status", new
            {
                jobId = job.Id,
                status = job.Status.ToString()
            });

            while (!ct.IsCancellationRequested)
            {
                job = jobStore.GetJob(jobId);
                if (job is null)
                {
                    await WriteSseEventAsync("error", new { message = "Job not found" });
                    break;
                }

                var events = jobStore.GetEvents(jobId);
                
                // Send only new events since lastIndex
                for (var i = lastIndex; i < events.Count; i++)
                {
                    var ev = events[i];

                    await WriteSseEventAsync(ev.Stage, new
                    {
                        timestamp = ev.Timestamp,
                        stage = ev.Stage,
                        message = ev.Message
                    });
                }

                lastIndex = events.Count;

                if (job.Status is ResearchJobStatus.Completed or ResearchJobStatus.Failed)
                {
                    // Final event with summary info
                    await WriteSseEventAsync("completed", new
                    {
                        jobId = job.Id,
                        status = job.Status.ToString(),
                        reportMarkdown = job.ReportMarkdown,
                        visitedUrls = job.VisitedUrls
                    });
                    break;
                }

                // Poll interval
                await Task.Delay(TimeSpan.FromSeconds(1), ct);
            }
        });

        app.MapGet("/api/research/jobs/{jobId}", async (Guid jobId, 
            [FromServices] IResearchJobStore jobStore) =>
        {
            var job = jobStore.GetJob(jobId);
            if (job == null)
            {
                return Results.NotFound();
            }
            
            return Results.Ok(job);
        });
    }
}

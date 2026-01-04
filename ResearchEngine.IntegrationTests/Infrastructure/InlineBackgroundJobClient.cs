using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.Extensions.DependencyInjection;
using ResearchEngine.Domain;


namespace ResearchEngine.IntegrationTests.Infrastructure;

/// <summary>
/// WithWebHostBuilder overrides apply to the web host that handles HTTP.
/// The “real” background runner is the fixture Hangfire server, which is not rebuilt per test and does not use the per-test DI.
/// By replacing IBackgroundJobClient, we prevent the job from ever leaving the test host.
/// It runs inside the same DI graph, so our overridden ISearchClient / IChatModel is actually used.
/// </summary>
public sealed class InlineBackgroundJobClient : IBackgroundJobClient
{
    private readonly IServiceProvider _services;

    public InlineBackgroundJobClient(IServiceProvider services)
        => _services = services;

    public string Create(Job job, IState state)
    {
        if (job is null) throw new ArgumentNullException(nameof(job));
        if (job.Args is null || job.Args.Count != 1 || job.Args[0] is not Guid jobId)
            throw new InvalidOperationException($"InlineBackgroundJobClient expected a single Guid arg, but got: {Describe(job)}");

        // Fire-and-forget: mimic Hangfire's "do not tie to request cancellation"
        _ = Task.Run(async () =>
        {
            using var scope = _services.CreateScope();
            var orchestrator = scope.ServiceProvider.GetRequiredService<IResearchOrchestrator>();

            // No cancellation (matches your RunJobBackgroundAsync)
            await orchestrator.RunJobBackgroundAsync(jobId);
        });

        // Return some deterministic id; it's not used by your tests
        return $"inline:{Guid.NewGuid():N}";
    }

    public bool ChangeState(string jobId, IState state, string expectedState) => false;
    public bool Delete(string jobId) => false;
    public bool Requeue(string jobId) => false;

    private static string Describe(Job job)
        => $"{job.Type?.Name}.{job.Method?.Name}({string.Join(", ", job.Args ?? new List<object>())})";
}

namespace ResearchApi.Domain;

public interface IReportSynthesisService
{
    Task<string> WriteFinalReportAsync(
        ResearchJob job,
        string clarificationsText,
        IEnumerable<Learning> learnings,
        CancellationToken ct);
}

namespace ResearchApi.Domain;

public interface ILearningExtractionService
{
    Task<IReadOnlyList<ExtractedLearningItemWithEvidence>> ExtractLearningsAsync(
        string query,
        string clarificationsText,
        string sourceContent,
        string sourceUrl,
        string targetLanguage,
        CancellationToken ct);
}
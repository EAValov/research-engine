namespace ResearchApi.Domain;

public interface ILearningExtractionService
{
    Task<IReadOnlyList<ExtractedLearningItem>> ExtractLearningsAsync(
        string query,
        string clarificationsText,
        ScrapedPage page,
        string sourceUrl,
        string targetLanguage,
        CancellationToken ct);
}

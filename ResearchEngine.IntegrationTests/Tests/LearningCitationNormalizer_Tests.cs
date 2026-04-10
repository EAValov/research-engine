using ResearchEngine.Infrastructure;

namespace ResearchEngine.IntegrationTests.Tests;

public sealed class LearningCitationNormalizer_Tests
{
    [Fact]
    public void Normalize_ConvertsFullwidthBracketCitations_ToAsciiCitations()
    {
        const string input =
            "Recent analysis cites financial stability concerns【lrn:3562ed21521c42f29007064a4393513b】 and AML overhead【lrn:f1254b9d6ccf4edc8a87acc439b9f5e4】.";

        var normalized = LearningCitationNormalizer.Normalize(input);

        Assert.Contains("[lrn:3562ed21521c42f29007064a4393513b]", normalized);
        Assert.Contains("[lrn:f1254b9d6ccf4edc8a87acc439b9f5e4]", normalized);
        Assert.DoesNotContain("【lrn:", normalized);
    }

    [Fact]
    public void Normalize_WrapsStandaloneBareCitationTokens()
    {
        const string input =
            "High fees are a repeated adoption barrier lrn:15f6dad87a5e4c78ac49fb2fd6a77ae1.";

        var normalized = LearningCitationNormalizer.Normalize(input);

        Assert.Contains("[lrn:15f6dad87a5e4c78ac49fb2fd6a77ae1]", normalized);
    }

    [Fact]
    public void Normalize_DoesNotRewriteUrlSegmentsThatContainLrnPrefix()
    {
        const string input =
            "Reference URL: https://example.com/lrn:15f6dad87a5e4c78ac49fb2fd6a77ae1/resource";

        var normalized = LearningCitationNormalizer.Normalize(input);

        Assert.Equal(input, normalized);
    }

    [Fact]
    public void Normalize_PreservesCitationLabels_WhenBracketStyleIsMalformed()
    {
        const string input =
            "See the grouped reference 【lrn:3562ed21521c42f29007064a4393513b|12】 for the original evidence.";

        var normalized = LearningCitationNormalizer.Normalize(input);

        Assert.Contains("[lrn:3562ed21521c42f29007064a4393513b|12]", normalized);
    }

    [Fact]
    public void Normalize_RepairsTruncatedCitationIds_WhenKnownLearningIdsProvideUniqueMatch()
    {
        var knownIds = new[]
        {
            Guid.ParseExact("e95686f743ff4e8695ec661098d33af1", "N")
        };

        const string input =
            "The first confirmation typically arrives 5-10 minutes after broadcast【lrn:e95686f743ff4e8695ec661098d33a】.";

        var normalized = LearningCitationNormalizer.Normalize(input, knownIds);

        Assert.Contains("[lrn:e95686f743ff4e8695ec661098d33af1]", normalized);
    }

    [Fact]
    public void Normalize_DoesNotGuess_WhenMultipleKnownLearningIdsAreEquallyClose()
    {
        var knownIds = new[]
        {
            Guid.ParseExact("e95686f743ff4e8695ec661098d33af1", "N"),
            Guid.ParseExact("e95686f743ff4e8695ec661098d33af2", "N")
        };

        const string input =
            "The first confirmation typically arrives after broadcast【lrn:e95686f743ff4e8695ec661098d33a】.";

        var normalized = LearningCitationNormalizer.Normalize(input, knownIds);

        Assert.Contains("【lrn:e95686f743ff4e8695ec661098d33a】", normalized);
    }
}

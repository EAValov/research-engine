using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ResearchApi.Application;
using ResearchApi.Configuration;
using ResearchApi.Domain;
using ResearchApi.Infrastructure;
using Xunit;

namespace ResearchApi.IntegrationTests;

public sealed class LearningExtractionServiceIntegrationTests : IClassFixture<LlmIntegrationFixture>
{
    private readonly LlmIntegrationFixture _fx;

    public LearningExtractionServiceIntegrationTests(LlmIntegrationFixture fx)
    {
        _fx = fx;
    }

    [Fact]
    public async Task ExtractLearningsAsync_Returns_Learnings_From_Content()
    {
        // Arrange
        var service = new LearningExtractionService(
            _fx.LlmService,
            _fx.ChunkingOptions,
            NullLogger<LearningExtractionService>.Instance);

        var page = new ScrapedPage
        {
            Id          = Guid.NewGuid(),
            Url         = "https://example.com/test-article",
            Content     = """
                          ## KI in der bayerischen Industrie

                          Bayern investiert stark in KI-gestützte Produktionsautomatisierung.
                          Insbesondere Automotive- und Maschinenbau-Cluster nutzen KI für
                          prädiktive Wartung, Qualitätskontrolle und digitale Zwillinge.

                          Förderprogramme wie die Hightech Agenda Bayern beschleunigen
                          die Einführung solcher Technologien insbesondere bei KMU.
                          """,
            Language    = "de",
            Region      = "DE",
            CreatedAt   = DateTimeOffset.UtcNow
        };

        const string query = "Wie nutzen bayerische Industrieunternehmen KI in der Produktion?";
        var clarificationsText = "Fokus auf Automotive / Maschinenbau in Bayern und Rolle von Förderprogrammen.";
        var targetLanguage = "de";

        // Act
        var learnings = await service.ExtractLearningsAsync(
            query,
            clarificationsText,
            page,
            page.Url!,
            targetLanguage,
            CancellationToken.None);

        // Assert
        Assert.NotNull(learnings);
        Assert.NotEmpty(learnings);

        foreach (var l in learnings)
        {
            Assert.False(string.IsNullOrWhiteSpace(l.Text));
        }

        // Sanity-check: at least one learning should mention KI or Bayern
        Assert.Contains(learnings, l =>
            l.Text.Contains("KI", StringComparison.OrdinalIgnoreCase) ||
            l.Text.Contains("Bayern", StringComparison.OrdinalIgnoreCase));
    }
}

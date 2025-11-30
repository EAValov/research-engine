using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ResearchApi.Domain;
using ResearchApi.Infrastructure;

namespace ResearchApi.IntegrationTests;

[Collection("LlmIntegration")]
public class AutoSelectLanguageRegionTests
{
    private readonly ILlmService _llmService;

    public AutoSelectLanguageRegionTests(LlmIntegrationFixture fixture)
    {
        _llmService = fixture.LlmService;
    }

    [Fact(DisplayName = "AutoSelectLanguageRegionAsync picks de/DE for clearly German, Bavaria-focused query")]
    public async Task AutoSelectLanguageRegion_GermanQuery_ReturnsDeDe()
    {
        // arrange
        var query = "Wie entwickelt sich der Markt für KI-gestützte Produktionsautomatisierung in Bayern im Zeitraum 2026–2030?";
        var clarifications = new List<Clarification>
        {
            new() { Question = "Region focus", Answer = "Bayern, Deutschland" }
        };

        var ct = CancellationToken.None;

        // act
        var (language, region) = await DeepResearchChatHandler.AutoSelectLanguageRegionAsync(
            query,
            clarifications,
            _llmService,
            ct);

        // assert
        Assert.Equal("de", language);
        Assert.Contains("Germany", region);
    }

    [Fact(DisplayName = "AutoSelectLanguageRegionAsync picks en(/US) for global English query")]
    public async Task AutoSelectLanguageRegion_GlobalEnglish_ReturnsEn()
    {
        // arrange
        var query = "What are the global trends in LLM infrastructure and GPU supply between 2025 and 2030?";
        var clarifications = new List<Clarification>
        {
            new() { Question = "Region focus", Answer = "Global markets, with emphasis on US and EU" }
        };

        var ct = CancellationToken.None;

        // act
        var (language, region) = await DeepResearchChatHandler.AutoSelectLanguageRegionAsync(
            query,
            clarifications,
            _llmService,
            ct);

        // assert
        Assert.Equal("en", language);
        
        // region might be null or "US" depending on your prompt – accept both
        if (region is not null)
        {
            Assert.Equal("United States", region);
        }
    }
}

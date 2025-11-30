using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ResearchApi.Domain;
using ResearchApi.Infrastructure;

namespace ResearchApi.IntegrationTests;

[Collection("LlmIntegration")]
public class AutoSelectBreadthDepthTests
{
    private readonly ILlmService _llmService;

    public AutoSelectBreadthDepthTests(LlmIntegrationFixture fixture)
    {
        _llmService = fixture.LlmService;
    }

    [Fact(DisplayName = "AutoSelectBreadthDepthAsync returns values in valid range")]
    public async Task AutoSelectBreadthDepth_ReturnsValuesInRange()
    {
        // arrange
        var query = "What does it take to build a subscription-based cloud photo storage startup competing with iCloud and Google Photos?";
        var clarifications = new List<Clarification>
        {
            new() { Question = "Focus", Answer = "Regulatory, business model, and technical scaling aspects" }
        };

        var ct = CancellationToken.None;

        // act
        var (breadth, depth) = await DeepResearchChatHandler.AutoSelectBreadthDepthAsync(
            query,
            clarifications,
            _llmService,
            ct);

        // assert: respect your clamps: breadth 1..8, depth 1..4
        Assert.InRange(breadth, 1, 8);
        Assert.InRange(depth, 1, 4);
    }

    [Fact(DisplayName = "AutoSelectBreadthDepthAsync chooses reasonably large breadth for multi-aspect query")]
    public async Task AutoSelectBreadthDepth_BroadQuery_PrefersHigherBreadth()
    {
        // arrange
        var query = "What are the regulatory, market, technical, and funding risks for launching an AI-powered consumer health app in the EU and US?";
        var clarifications = new List<Clarification>
        {
            new() { Question = "Aspects", Answer = "Regulation, market size, competition, infrastructure, funding" }
        };

        var ct = CancellationToken.None;

        // act
        var (breadth, depth) = await DeepResearchChatHandler.AutoSelectBreadthDepthAsync(
            query,
            clarifications,
            _llmService,
            ct);

        // assert
        Assert.InRange(breadth, 2, 8); // expect at least 2
        Assert.InRange(depth, 1, 4);   // just validate range
    }
}

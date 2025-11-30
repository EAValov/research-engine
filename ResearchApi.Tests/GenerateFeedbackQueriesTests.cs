using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace ResearchApi.IntegrationTests;

[Collection("LlmIntegration")]
public class GenerateFeedbackQueriesTests
{
    private readonly LlmIntegrationFixture _fixture;

    public GenerateFeedbackQueriesTests(LlmIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact(DisplayName = "GenerateFeedbackQueries returns up to max focused questions")]
    public async Task GenerateFeedbackQueries_ReturnsReasonableQuestions()
    {
        // arrange
        const bool includeBreadthDepthQuestions = true;
        var query = "What are the regulatory risks and market opportunities for AI-powered health apps in the EU and US?";

        var ct = CancellationToken.None;

        // act
        var questions = await DeepResearchChatHandler.GenerateFeedbackQueries(
            query,
            includeBreadthDepthQuestions,
            _fixture.LlmService,
            ct);

        // assert basic shape
        Assert.NotNull(questions);
        Assert.NotEmpty(questions);

        // All questions non-empty
        Assert.All(questions, q => Assert.False(string.IsNullOrWhiteSpace(q)));

        // Reasonable heuristic: most lines end with ? 
        var questionLikeCount = questions.Count(q => q.Trim().EndsWith("?"));
        Assert.True(questionLikeCount >= questions.Count / 2,
            "At least half of feedback lines should look like questions.");

        // If breadth/depth requested, expect at least one reference to breadth/depth/configure
        if (includeBreadthDepthQuestions)
        {
            var hasBreadthDepth = questions.Any(q =>
                q.Contains("Broad", System.StringComparison.OrdinalIgnoreCase) ||
                q.Contains("Deep", System.StringComparison.OrdinalIgnoreCase));

            Assert.True(hasBreadthDepth,
                "Expected at least one clarification question mentioning breadth/depth.");
        }
    }
}

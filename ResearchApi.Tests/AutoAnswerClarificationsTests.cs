using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ResearchApi.Domain;
using ResearchApi.Infrastructure; // Clarification

namespace ResearchApi.IntegrationTests;

[Collection("LlmIntegration")]
public class AutoAnswerClarificationsTests
{
    private readonly ILlmService _llmService;

    public AutoAnswerClarificationsTests(LlmIntegrationFixture fixture)
    {
        _llmService = fixture.LlmService;
    }

    [Fact(DisplayName = "AutoAnswerClarificationsAsync returns one answer per question")]
    public async Task AutoAnswerClarifications_ReturnsAnswersPerQuestion()
    {
        // arrange
        var query = "What are the regulatory risks and market opportunities for AI-powered health apps in the EU and US?";
        var questions = new List<string>
        {
            "Is the app intended primarily for wellness or for diagnosis/treatment?",
            "Will the app process any special category data under GDPR (e.g. health records)?",
            "Should we focus more on EU or US regulation in the analysis?"
        };

        var ct = CancellationToken.None;

        // act
        var clarifications = await DeepResearchChatHandler.AutoAnswerClarificationsAsync(
            query,
            questions,
            _llmService,
            ct);

        // assert
        Assert.NotNull(clarifications);
        Assert.Equal(questions.Count, clarifications.Count);

        // Each clarification should carry the question and non-empty answer
        for (int i = 0; i < questions.Count; i++)
        {
            Assert.Equal(questions[i], clarifications[i].Question);
            Assert.False(string.IsNullOrWhiteSpace(clarifications[i].Answer));
        }

        // Sanity check: at least one answer should mention "EU" or "US"
        Assert.Contains(clarifications, c =>
            c.Answer.Contains("EU", System.StringComparison.OrdinalIgnoreCase) ||
            c.Answer.Contains("US", System.StringComparison.OrdinalIgnoreCase));
    }
}

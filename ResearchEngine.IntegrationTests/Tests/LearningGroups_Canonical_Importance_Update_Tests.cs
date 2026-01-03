using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Json;
using System.Text.Json;
using ResearchEngine.IntegrationTests.Helpers;
using ResearchEngine.IntegrationTests.Infrastructure;
using ResearchEngine.Domain;
using ResearchEngine.Infrastructure;

namespace ResearchEngine.IntegrationTests.Tests;

[Collection(ContainersCollection.Name)]
public sealed class LearningGroups_Canonical_Importance_Update_Tests : IntegrationTestBase
{
    public LearningGroups_Canonical_Importance_Update_Tests(ContainersFixture containers) : base(containers) { }

    [Fact]
    public async Task AddLearning_HigherImportance_UpdatesGroupCanonicalScore_AndStats()
    {
        using var client = CreateClient();

        // 1) Create job + wait completion
        var jobId = await CreateJobAsync(client, "Test query: canonical importance update.");
        var (status, _, _) = await SseTestHelpers.WaitForDoneAsync(client, jobId, TimeSpan.FromSeconds(60));
        Assert.Equal("Completed", status);

        // Deterministic grouping under FakeEmbeddingModel + threshold requires same exact text.
        const string text = "User learning: dedup groups should keep the strongest canonical statement.";

        // 2) Add first learning low score
        var add1 = new
        {
            text,
            importanceScore = 0.2f,
            reference = (string?)null,
            evidenceText = (string?)null,
            language = (string?)null,
            region = (string?)null
        };

        var r1 = await client.PostAsJsonAsync($"/api/research/jobs/{jobId}/learnings", add1);
        r1.EnsureSuccessStatusCode();

        var j1 = await r1.Content.ReadFromJsonAsync<JsonElement>();
        var groupId = j1.GetProperty("learning").GetProperty("learningGroupId").GetGuid();
        Assert.NotEqual(Guid.Empty, groupId);

        // 3) Add second learning with same text but higher score => should update canonical importance
        var add2 = new
        {
            text,
            importanceScore = 0.95f,
            reference = (string?)null,
            evidenceText = (string?)null,
            language = (string?)null,
            region = (string?)null
        };

        var r2 = await client.PostAsJsonAsync($"/api/research/jobs/{jobId}/learnings", add2);
        r2.EnsureSuccessStatusCode();

        // 4) Read group from DB and assert canonical + stats updated
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ResearchDbContext>();

        var group = await db.Set<LearningGroup>()
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == groupId);

        Assert.NotNull(group);

        Assert.Equal(text, group!.CanonicalText);
        Assert.True(group.CanonicalImportanceScore >= 0.95f, "Canonical score should reflect the strongest member learning.");
        Assert.True(group.MemberCount >= 2, "Group stats should reflect at least 2 learnings.");
        Assert.True(group.DistinctSourceCount >= 1);
    }
}

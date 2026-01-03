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
public sealed class LearningGroups_Canonical_Update_Tests : IntegrationTestBase
{
    public LearningGroups_Canonical_Update_Tests(ContainersFixture containers) : base(containers) { }

    [Fact]
    public async Task AddLearning_HigherImportanceInSameGroup_UpdatesCanonical()
    {
        using var client = CreateClient();

        // 1) Create job + wait completion
        var jobId = await CreateJobAsync(client, "Test query: canonical update.");
        var (status, _, _) = await SseTestHelpers.WaitForDoneAsync(client, jobId, TimeSpan.FromSeconds(60));
        Assert.Equal("Completed", status);

        // We want to ensure it stays in the same group. Easiest: identical text embedding.
        // But we also want canonical text to change => use different text BUT SAME embedding won't happen with FakeEmbeddingModel.
        // So we use identical text to guarantee group match and assert canonical importance score update.
        //
        // If your grouping threshold allows near-duplicates to group together reliably, you can switch text2 to a paraphrase.
        const string text = "User learning: dedup groups should prefer the best phrased canonical statement.";

        // 2) Add first learning low score
        var add1 = new { text, importanceScore = 0.2f, reference = (string?)null, evidenceText = (string?)null, language = (string?)null, region = (string?)null };
        var r1 = await client.PostAsJsonAsync($"/api/research/jobs/{jobId}/learnings", add1);
        r1.EnsureSuccessStatusCode();

        var j1 = await r1.Content.ReadFromJsonAsync<JsonElement>();
        var groupId = j1.GetProperty("learning").GetProperty("learningGroupId").GetGuid();
        Assert.NotEqual(Guid.Empty, groupId);

        // 3) Add second learning with same text but higher score => canonical importance should update
        var add2 = new { text, importanceScore = 0.95f, reference = (string?)null, evidenceText = (string?)null, language = (string?)null, region = (string?)null };
        var r2 = await client.PostAsJsonAsync($"/api/research/jobs/{jobId}/learnings", add2);
        r2.EnsureSuccessStatusCode();

        // 4) Load group from DB and assert canonical score updated
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ResearchDbContext>();

        var group = await db.Set<LearningGroup>()
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == groupId);

        Assert.NotNull(group);

        // CanonicalText should still be text (because we used identical), but score should reflect the highest.
        Assert.Equal(text, group!.CanonicalText);
        Assert.True(group.CanonicalImportanceScore >= 0.95f);
        Assert.True(group.MemberCount >= 2);
    }
}

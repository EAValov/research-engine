using System.Text.RegularExpressions;
using ResearchEngine.IntegrationTests.Infrastructure;

namespace ResearchEngine.IntegrationTests.Tests;

[Collection(ContainersCollection.Name)]
public sealed class OpenApi_Learnings_Contract_Tests : IntegrationTestBase
{
    public OpenApi_Learnings_Contract_Tests(ContainersFixture containers) : base(containers) { }

    [Fact]
    public async Task AddLearning_OpenApiContract_Declares201Created()
    {
        using var client = CreateClient();

        var resp = await client.GetAsync("/openapi/v1.yaml");
        resp.EnsureSuccessStatusCode();

        var yaml = await resp.Content.ReadAsStringAsync();

        Assert.Matches(
            new Regex(
                @"'/api/jobs/\{jobId\}/learnings':.*?post:.*?responses:\s*'201':",
                RegexOptions.Singleline | RegexOptions.CultureInvariant),
            yaml);
    }
}

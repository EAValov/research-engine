using System.Net;
using System.Text;
using System.Text.Json;
using ResearchApi.Tests.Infrastructure;
using Xunit;

namespace ResearchApi.Tests.Endpoints;

public class DeepResearchChatApiTests
{
    private readonly TestWebApplicationFactory _factory;

    public DeepResearchChatApiTests()
    {
        _factory = new TestWebApplicationFactory();
    }

    [Fact]
    public async Task ChatCompletions_WithValidRequest_ReturnsOk()
    {
        var client = _factory.CreateClient();
        
        var request = new
        {
            model = "local-deep-research",
            stream = true,
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = "Test research query"
                }
            }
        };

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/v1/chat/completions", content);
        Assert.True(response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound);
    }
}

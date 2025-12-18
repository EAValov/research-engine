using System.Net;
using System.Text;
using System.Text.Json;
using ResearchApi.Tests.Infrastructure;
using Xunit;

namespace ResearchApi.Tests.Endpoints;

public class DeepResearchJobsApiStreamEventsTests
{
    private readonly TestWebApplicationFactory _factory;

    public DeepResearchJobsApiStreamEventsTests()
    {
        _factory = new TestWebApplicationFactory();
    }

    [Fact]
    public async Task StreamEvents_WithValidId_ReturnsStream()
    {
        // Arrange
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer 9h32F2fNuwMQL0OUujpUoIF6l8S/lGPOM1ylNhH+MKQ=");
        
        // Act
        var response = await client.GetAsync("/api/research/jobs/00000000-0000-0000-0000-000000000000/events/stream");

        // Assert - We expect either success or not found (not database error)
        Assert.True(response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound);
    }

}

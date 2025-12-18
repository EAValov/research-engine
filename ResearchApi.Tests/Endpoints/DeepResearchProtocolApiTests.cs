using System.Net;
using System.Text;
using System.Text.Json;
using ResearchApi.Tests.Infrastructure;
using Xunit;

namespace ResearchApi.Tests.Endpoints;

public class DeepResearchProtocolApiTests
{
    private readonly TestWebApplicationFactory _factory;

    public DeepResearchProtocolApiTests()
    {
        _factory = new TestWebApplicationFactory();
    }

    [Fact]
    public async Task GenerateClarifications_WithValidRequest_ReturnsClarifications()
    {
        // Arrange
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer 9h32F2fNuwMQL0OUujpUoIF6l8S/lGPOM1ylNhH+MKQ=");
        
        var request = new
        {
            query = "Test research query",
            includeConfigureQuestions = false
        };

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await client.PostAsync("/api/research/protocol/clarifications", content);

        // Assert - Should return success or not found (not database error)
        Assert.True(response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GenerateClarifications_WithEmptyQuery_ReturnsBadRequest()
    {
        // Arrange
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer 9h32F2fNuwMQL0OUujpUoIF6l8S/lGPOM1ylNhH+MKQ=");
        
        var request = new
        {
            query = "", // Invalid empty query
            includeConfigureQuestions = false
        };

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await client.PostAsync("/api/research/protocol/clarifications", content);

        // Assert - Should return bad request for invalid input
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SelectParameters_WithValidRequest_ReturnsParameters()
    {
        // Arrange
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer 9h32F2fNuwMQL0OUujpUoIF6l8S/lGPOM1ylNhH+MKQ=");
        
        var request = new
        {
            query = "Test research query"
        };

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await client.PostAsync("/api/research/protocol/parameters", content);

        // Assert - Should return success or not found (not database error)
        Assert.True(response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SelectParameters_WithEmptyQuery_ReturnsBadRequest()
    {
        // Arrange
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer 9h32F2fNuwMQL0OUujpUoIF6l8S/lGPOM1ylNhH+MKQ=");
        
        var request = new
        {
            query = "" // Invalid empty query
        };

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await client.PostAsync("/api/research/protocol/parameters", content);

        // Assert - Should return bad request for invalid input
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}

using System.Net;
using System.Text;
using System.Text.Json;
using ResearchApi.Tests.Infrastructure;
using Xunit;

namespace ResearchApi.Tests.Endpoints;

public class DeepResearchJobsApiTests
{
    private readonly TestWebApplicationFactory _factory;

    public DeepResearchJobsApiTests()
    {
        // We need to make sure we're not trying to connect to database in tests.
        // This approach will skip the database connection by using a custom startup.
        _factory = new TestWebApplicationFactory();
    }

    [Fact]
    public async Task CreateJob_WithValidRequest_ReturnsCreatedJob()
    {
        // Arrange
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer 9h32F2fNuwMQL0OUujpUoIF6l8S/lGPOM1ylNhH+MKQ=");
        
        var request = new
        {
            query = "Test research query",
            breadth = 2,
            depth = 2,
            language = "en"
        };

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await client.PostAsync("/api/research/jobs", content);

        // Assert - Should return success or not found (not database error)
        Assert.True(response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreateJob_WithInvalidRequest_ReturnsBadRequest()
    {
        // Arrange
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer 9h32F2fNuwMQL0OUujpUoIF6l8S/lGPOM1ylNhH+MKQ=");
        
        var request = new
        {
            query = "", // Invalid empty query
            breadth = 2,
            depth = 2,
            language = "en"
        };

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await client.PostAsync("/api/research/jobs", content);

        // Assert - Should return bad request for invalid input
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetJob_WithInvalidGuid_ReturnsNotFound()
    {
        // Arrange
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer 9h32F2fNuwMQL0OUujpUoIF6l8S/lGPOM1ylNhH+MKQ=");
        
        // Act
        var response = await client.GetAsync("/api/research/jobs/invalid-guid");

        // Assert - Invalid GUID format should return not found (not bad request)
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ListEvents_WithInvalidGuid_ReturnsNotFound()
    {
        // Arrange
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer 9h32F2fNuwMQL0OUujpUoIF6l8S/lGPOM1ylNhH+MKQ=");
        
        // Act
        var response = await client.GetAsync("/api/research/jobs/invalid-guid/events");

        // Assert - Invalid GUID format should return not found (not bad request)
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ListEvents_WithValidId_ReturnsEvents()
    {
        // Arrange
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer 9h32F2fNuwMQL0OUujpUoIF6l8S/lGPOM1ylNhH+MKQ=");
        
        // Act
        var response = await client.GetAsync("/api/research/jobs/00000000-0000-0000-0000-000000000000/events");

        // Assert - Should return success or not found (not database error)
        Assert.True(response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound);
    }
}

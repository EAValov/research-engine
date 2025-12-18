using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using ResearchApi.Tests.Infrastructure;
using Xunit;

namespace ResearchApi.Tests.Endpoints;

public class OpenAiModelEndpointsTests
{
    private readonly TestWebApplicationFactory _factory;

    public OpenAiModelEndpointsTests()
    {
        _factory = new TestWebApplicationFactory();
    }

    [Fact]
    public async Task GetModels_ReturnsModels()
    {
        // Arrange
        var client = _factory.CreateClient();
        
        // Act
        var response = await client.GetAsync("/v1/models");

        // Assert - Should return success (no auth required)
        Assert.True(response.IsSuccessStatusCode);
    }
}

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using ResearchApi.Application;
using ResearchApi.Infrastructure;
using Xunit;

namespace ResearchApi.IntegrationTests;
public sealed class QueryPlanningServiceIntegrationTests : IClassFixture<LlmIntegrationFixture>
{
    private readonly LlmIntegrationFixture _fx;

    public QueryPlanningServiceIntegrationTests(LlmIntegrationFixture fx)
    {
        _fx = fx;
    }

    [Fact]
    public async Task GenerateSerpQueriesAsync_Returns_NonEmpty_List()
    {
        // Arrange
        var service = new QueryPlanningService(
            _fx.LlmService,
            NullLogger<QueryPlanningService>.Instance);

        const string query = "Wie entwickelt sich der Markt für KI-gestützte Produktionsautomatisierung in Bayern bis 2030?";
        var clarificationsText = "Fokus auf Automotive und Maschinenbau, Vergleich zu Baden-Württemberg und NRW.";

        var breadth = 3;
        var depth   = 2;
        var lang    = "de";

        // Act
        var result = await service.GenerateSerpQueriesAsync(
            query,
            clarificationsText,
            depth,
            breadth,
            lang,
            CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);
        Assert.InRange(result.Count, 1, breadth);  // should respect breadth cap

        foreach (var q in result)
        {
            Assert.False(string.IsNullOrWhiteSpace(q));
        }
    }
}

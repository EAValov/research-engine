using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ResearchEngine.Infrastructure;
using Xunit;

namespace ResearchEngine.IntegrationTests.Infrastructure;

[Collection(ContainersCollection.Name)]
public abstract class IntegrationTestBase : IClassFixture<ContainersFixture>, IDisposable
{
    protected readonly ContainersFixture Containers;
    protected readonly CustomWebApplicationFactory Factory;

    protected IntegrationTestBase(ContainersFixture containers)
    {
        Containers = containers;
        Factory = new CustomWebApplicationFactory(Containers);

        using var scope = Factory.Services.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ResearchDbContext>>();
        using var db = dbFactory.CreateDbContext();
        db.Database.Migrate();
    }

    protected HttpClient CreateClient()
        => Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

    public void Dispose()
    {
        Factory.Dispose();
    }
}

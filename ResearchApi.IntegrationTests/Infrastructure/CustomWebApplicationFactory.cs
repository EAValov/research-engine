using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ResearchApi.Domain;
using ResearchApi.Infrastructure;
using StackExchange.Redis;

namespace ResearchApi.IntegrationTests.Infrastructure;

public sealed class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly ContainersFixture _containers;

    public CustomWebApplicationFactory(ContainersFixture containers)
    {
        _containers = containers;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((context, cfg) =>
        {
            var redisHost = _containers.Redis.Hostname;
            var redisPort = _containers.Redis.GetMappedPublicPort(6379);
            var redisConn = $"{redisHost}:{redisPort},abortConnect=false";

            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:ResearchDb"] = _containers.Postgres.GetConnectionString(),
                ["RedisEventBusOptions:ConnectionString"] = redisConn,

                ["EmbeddingConfig:Endpoint"] = "http://fake",
                ["EmbeddingConfig:ApiKey"] = "fake",
                ["EmbeddingConfig:ModelId"] = "fake",
                ["EmbeddingConfig:Dimension"] = "1024"
            });
        });

        builder.ConfigureServices(services =>
        {
            // Replace external dependencies with fakes
            services.RemoveAll<ISearchClient>();
            services.RemoveAll<ICrawlClient>();
            services.RemoveAll<IChatModel>();
            services.RemoveAll<IEmbeddingModel>();
            services.RemoveAll<ITokenizer>();

            services.AddSingleton<ISearchClient, FakeSearchClient>();
            services.AddSingleton<ICrawlClient, FakeCrawlClient>();
            services.AddSingleton<IEmbeddingModel, FakeEmbeddingModel>();
            services.AddSingleton<ITokenizer, FakeTokenizer>();

            // allows us to override the IChatModel impelmentation
            services.AddSingleton<FakeChatModel>();
            services.AddSingleton<IChatModel>(sp => sp.GetRequiredService<FakeChatModel>());        

            services.RemoveAll<IConnectionMultiplexer>();

            var redisHost = _containers.Redis.Hostname;
            var redisPort = _containers.Redis.GetMappedPublicPort(6379);

            services.AddSingleton<IConnectionMultiplexer>(_ =>
            {
                var opts = new ConfigurationOptions
                {
                    AbortOnConnectFail = false,
                    ConnectRetry = 10,
                    ConnectTimeout = 10000,
                    SyncTimeout = 15000,
                    KeepAlive = 10,
                };

                opts.EndPoints.Add(redisHost, redisPort);

                // Optional but helps local/podman DNS oddities
                opts.ResolveDns = false;

                return ConnectionMultiplexer.Connect(opts);
            });

            // Ensure DbContext uses container connection string.
            // Your Program likely registers this already; we remove and re-add to be safe.
            services.RemoveAll<DbContextOptions<ResearchDbContext>>();
            services.RemoveAll<IDbContextFactory<ResearchDbContext>>();

            services.AddDbContextFactory<ResearchDbContext>(opt =>
            {
                opt.UseNpgsql(_containers.Postgres.GetConnectionString());
            });

            // Run migrations once host is built
            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ResearchDbContext>>();
            using var db = dbFactory.CreateDbContext();
            db.Database.Migrate();

            services.AddAuthentication(options => {
                options.DefaultAuthenticateScheme = TestAuthHandler.Scheme;
                options.DefaultChallengeScheme = TestAuthHandler.Scheme;
            }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                TestAuthHandler.Scheme, _ => { });

            // Ensure every endpoint sees an authenticated user by default
            services.AddAuthorization(options =>
            {
                options.FallbackPolicy = new AuthorizationPolicyBuilder()
                    .AddAuthenticationSchemes(TestAuthHandler.Scheme)
                    .RequireAuthenticatedUser()
                    .Build();
            });
        });
    }
}

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ResearchEngine.Domain;
using ResearchEngine.Infrastructure;
using StackExchange.Redis;

namespace ResearchEngine.IntegrationTests.Infrastructure;

public sealed class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly ContainersFixture _containers;
    private readonly string _dbConnectionString;
    private readonly int _redisDb;

    public CustomWebApplicationFactory(ContainersFixture containers, string dbConnectionString, int redisDb)
    {
        _containers = containers;
        _dbConnectionString = dbConnectionString;
        _redisDb = redisDb;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((context, cfg) =>
        {
            var redisHost = _containers.Redis.Hostname;
            var redisPort = _containers.Redis.GetMappedPublicPort(6379);

            // IMPORTANT: isolate per test via logical DB
            var redisConn = $"{redisHost}:{redisPort},abortConnect=false,defaultDatabase={_redisDb}";

            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:ResearchDb"] = _dbConnectionString,
                ["RedisEventBusOptions:ConnectionString"] = redisConn,

                ["EmbeddingConfig:Endpoint"] = "http://fake",
                ["EmbeddingConfig:ApiKey"] = "fake",
                ["EmbeddingConfig:ModelId"] = "fake",
                ["EmbeddingConfig:Dimension"] = "1024"
            });
        });

        builder.ConfigureServices(services =>
        {
            // fakes...
            services.RemoveAll<ISearchClient>();
            services.RemoveAll<ICrawlClient>();
            services.RemoveAll<IChatModel>();
            services.RemoveAll<IEmbeddingModel>();
            services.RemoveAll<ITokenizer>();

            services.AddSingleton<ISearchClient, FakeSearchClient>();
            services.AddSingleton<ICrawlClient, FakeCrawlClient>();
            services.AddSingleton<IEmbeddingModel, FakeEmbeddingModel>();
            services.AddSingleton<ITokenizer, FakeTokenizer>();

            services.AddSingleton<FakeChatModel>();
            services.AddSingleton<IChatModel>(sp => sp.GetRequiredService<FakeChatModel>());

            // Redis multiplexer (per-test DB)
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
                    ResolveDns = false,
                    DefaultDatabase = _redisDb, // redis db isolation
                };

                opts.EndPoints.Add(redisHost, redisPort);
                return ConnectionMultiplexer.Connect(opts);
            });

            // DbContext per-test DB
            services.RemoveAll<ResearchDbContext>();
            services.RemoveAll<DbContextOptions<ResearchDbContext>>();
            services.RemoveAll<IDbContextFactory<ResearchDbContext>>();

            services.AddDbContextFactory<ResearchDbContext>(opt =>
            {
                opt.UseNpgsql(_dbConnectionString, npgsql =>
                {
                    npgsql.UseVector();
                    npgsql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
                });
            });

            // auth
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = TestAuthHandler.Scheme;
                options.DefaultChallengeScheme = TestAuthHandler.Scheme;
            }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.Scheme, _ => { });

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

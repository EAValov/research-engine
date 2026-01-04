using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ResearchEngine.Domain;
using StackExchange.Redis;

namespace ResearchEngine.IntegrationTests.Infrastructure;

public sealed class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly ContainersFixture _containers;

    public CustomWebApplicationFactory(ContainersFixture containers)
        => _containers = containers;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
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

            // Redis multiplexer (single DB)
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
                    DefaultDatabase = 0,
                };

                opts.EndPoints.Add(redisHost, redisPort);
                return ConnectionMultiplexer.Connect(opts);
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

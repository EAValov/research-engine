using ResearchApi.Configuration;
using ResearchApi.Domain;     
using ResearchApi.Infrastructure;  

namespace ResearchApi.Bootstrap;

/// <summary>
/// Registers Firecrawl / SearxNG / Crawl4AI clients and their health checks.
/// </summary>
public static class SearchAndCrawlRegistrationExtensions
{
    public static void AddSearchAndCrawlClients(
        this IServiceCollection services,
        IConfiguration config)
    {
        // ----- Firecrawl -----
        var firecrawlSection    = config.GetSection(nameof(FirecrawlOptions));
        var firecrawlOptions    = firecrawlSection.Get<FirecrawlOptions>();
        var firecrawlConfigured = firecrawlOptions is not null &&
                                  !string.IsNullOrWhiteSpace(firecrawlOptions.BaseUrl);

        if (firecrawlConfigured)
        {
            services.Configure<FirecrawlOptions>(firecrawlSection);

            services
                .AddHttpClient<FirecrawlClient>()
                .ConfigureHttpClient(c =>
                {
                    c.Timeout = TimeSpan.FromSeconds(firecrawlOptions!.HttpClientTimeoutSeconds);
                    if (!string.IsNullOrWhiteSpace(firecrawlOptions.BaseUrl))
                    {
                        c.BaseAddress = new Uri(firecrawlOptions.BaseUrl);
                    }
                });
        }

        // ----- SearxNG -----
        var searxngSection    = config.GetSection(nameof(SearxngOptions));
        var searxngOptions    = searxngSection.Get<SearxngOptions>();
        var searxngConfigured = searxngOptions is not null &&
                                !string.IsNullOrWhiteSpace(searxngOptions.BaseUrl);

        if (searxngConfigured)
        {
            services.Configure<SearxngOptions>(searxngSection);

            services
                .AddHttpClient<SearxngSearchClient>()
                .ConfigureHttpClient(c =>
                {
                    c.BaseAddress = new Uri(searxngOptions!.BaseUrl);
                    if(searxngOptions!.HttpClientTimeoutSeconds.HasValue)
                    {
                        c.Timeout = TimeSpan.FromSeconds(searxngOptions!.HttpClientTimeoutSeconds.Value);
                    }
                });
        }

        // ----- Crawl4AI -----
        var crawl4AiSection    = config.GetSection(nameof(Crawl4AiOptions));
        var crawl4AiOptions    = crawl4AiSection.Get<Crawl4AiOptions>();
        var crawl4AiConfigured = crawl4AiOptions is not null &&
                                 !string.IsNullOrWhiteSpace(crawl4AiOptions.BaseUrl);

        if (crawl4AiConfigured)
        {
            services.Configure<Crawl4AiOptions>(crawl4AiSection);

            services
                .AddHttpClient<Crawl4AiClient>()
                .ConfigureHttpClient(c =>
                {
                    c.BaseAddress = new Uri(crawl4AiOptions!.BaseUrl);
                    if(crawl4AiOptions!.HttpClientTimeoutSeconds.HasValue)
                    {
                        c.Timeout = TimeSpan.FromSeconds(crawl4AiOptions!.HttpClientTimeoutSeconds.Value);
                    }
                });
        }

        // ISearchClient: Firecrawl > SearxNG
        if (firecrawlConfigured)
            services.AddScoped<ISearchClient, FirecrawlClient>(); 
        else if (searxngConfigured)
            services.AddScoped<ISearchClient, SearxngSearchClient>();
        else
            throw new InvalidOperationException(
                "No search backend configured. Please configure either SearxngOptions or FirecrawlOptions.");

        // ICrawlClient: Firecrawl > Crawl4AI
        if (firecrawlConfigured)
            services.AddScoped<ICrawlClient, FirecrawlClient>(); 
        else if (crawl4AiConfigured)
            services.AddScoped<ICrawlClient, Crawl4AiClient>();
        else
            throw new InvalidOperationException(
                "No crawl backend configured. Please configure either Crawl4AiOptions or FirecrawlOptions.");
    }

    /// <summary>
    /// Adds health checks for Firecrawl, SearxNG and Crawl4AI if they are configured.
    /// Call this *after* AddHealthChecks() in Program.cs.
    /// </summary>
    public static IHealthChecksBuilder AddSearchAndCrawlHealthChecks(
        this IHealthChecksBuilder healthChecks,
        IConfiguration config)
    {
        var searxngOptions      = config.GetSection(nameof(SearxngOptions)).Get<SearxngOptions>();
        var crawl4AiOptions     = config.GetSection(nameof(Crawl4AiOptions)).Get<Crawl4AiOptions>();
        var firecrawlOptions    = config.GetSection(nameof(FirecrawlOptions)).Get<FirecrawlOptions>();

        var firecrawlConfigured = firecrawlOptions is not null &&
                                  !string.IsNullOrWhiteSpace(firecrawlOptions.BaseUrl);

        if (firecrawlConfigured)
        {
            var firecrawlHealth = new Uri($"{firecrawlOptions!.BaseUrl.TrimEnd('/')}/liveness");
            healthChecks.AddUrlGroup(firecrawlHealth, "firecrawl", tags: ["ready", "search", "crawler"]);
        }

        var searxngConfigured = searxngOptions is not null &&
                                !string.IsNullOrWhiteSpace(searxngOptions.BaseUrl);

        var crawl4AiConfigured = crawl4AiOptions is not null &&
                                 !string.IsNullOrWhiteSpace(crawl4AiOptions.BaseUrl);

        if (searxngConfigured)
        {
            var searxngHealth = new Uri($"{searxngOptions!.BaseUrl.TrimEnd('/')}/healthz");
            healthChecks.AddUrlGroup(searxngHealth, "searxng", tags: ["ready", "search"]);
        }

        if (crawl4AiConfigured)
        {
            var crawl4AiHealth = new Uri($"{crawl4AiOptions!.BaseUrl.TrimEnd('/')}/health");
            healthChecks.AddUrlGroup(crawl4AiHealth, "crawl4ai", tags: ["ready", "crawler"]);
        }

        return healthChecks;
    }
}

using System.ComponentModel.DataAnnotations;
using ResearchApi.Domain;
using ResearchApi.Infrastructure;
using StackExchange.Redis;

namespace ResearchApi.Bootstrap;

public static class WebhookBootstrap
{
    public const string SectionName = "Webhook";

    public static bool AddOptionalWebhooks(this IServiceCollection services, IConfiguration config)
    {
        var section = config.GetSection(SectionName);
        if (!section.Exists())
            return false;

        // Bind once
        var options = section.Get<WebhookOptions>();
        if (options is null)
            return false;

        // DataAnnotations validation (cheap)
        var ctx = new ValidationContext(options);
        var results = new List<ValidationResult>();
        if (!Validator.TryValidateObject(options, ctx, results, validateAllProperties: true))
        {
            // No DI logger here; log later via app startup if you want.
            return false;
        }

        ConfigurationOptions parsed;
        
        try
        {
            parsed = ConfigurationOptions.Parse(options.RedisConnectionString);
        }
        catch
        {
            return false;
        }

        // Make the validated options available to DI (as a singleton instance)
        services.AddSingleton(options);

        // Also register parsed redis options
        services.AddSingleton(parsed);

        // Redis multiplexer
        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var redisOptions = sp.GetRequiredService<ConfigurationOptions>();
            return ConnectionMultiplexer.Connect(redisOptions);
        });

        // Optional services
        services.AddSingleton<IResearchEventBus, RedisResearchEventBus>();
        services.AddSingleton<IWebhookSubscriptionStore, RedisWebhookSubscriptionStore>();
        services.AddSingleton<IWebhookDispatcher, WebhookDispatcher>();

        // Webhook HttpClient (uses WebhookOptions instance)
        services.AddHttpClient<WebhookDispatcher>()
            .ConfigureHttpClient((sp, client) =>
            {
                var o = sp.GetRequiredService<WebhookOptions>();
                client.Timeout = TimeSpan.FromSeconds(o.HttpTimeoutSeconds);
            });

        return true;
    }

    public static void ConfigureJobStore(this IServiceCollection services, bool webhooksEnabled)
    {
        // Always register the concrete store
        services.AddScoped<PostgresResearchJobStore>();

        if (webhooksEnabled)
        {
            services.AddScoped<IResearchJobStore>(sp =>
                new PublishingResearchJobStore(
                    sp.GetRequiredService<PostgresResearchJobStore>(),
                    sp.GetRequiredService<IResearchEventBus>()));
        }
        else
        {
            services.AddScoped<IResearchJobStore>(sp => sp.GetRequiredService<PostgresResearchJobStore>());
        }
    }
}

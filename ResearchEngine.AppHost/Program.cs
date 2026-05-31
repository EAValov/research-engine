using Aspire.Hosting.Docker;
using Aspire.Hosting.ApplicationModel;
using static AppHostCompose;
using static AppHostTopology;

var options = AppHostOptions.Parse(args);
Environment.SetEnvironmentVariable("ASPIRE_CONTAINER_RUNTIME", options.ContainerRuntime);
var builder = DistributedApplication.CreateBuilder(args);

var researchPostgresUser = builder.AddParameter(
    "research-postgres-user",
    options.ResearchPostgresUser,
    publishValueAsDefault: true);
var researchPostgresPassword = builder.AddParameter(
    "research-postgres-password",
    options.ResearchPostgresPassword,
    secret: true);
var researchRedisPassword = builder.AddParameter(
    "research-redis-password",
    options.ResearchRedisPassword,
    secret: true);

var researchPostgres = builder.AddPostgres(
        options.ResearchPostgresResourceName,
        userName: researchPostgresUser,
        password: researchPostgresPassword)
    .WithImageReference(options.ResearchPostgresImage)
    .WithDataVolume(options.ResearchPostgresDataVolume)
    .PublishAsDockerComposeService((_, service) =>
    {
        SetServiceEnvironment(service, "POSTGRES_USER", options.ResearchPostgresUser);
        SetServiceEnvironment(service, "POSTGRES_PASSWORD", options.ResearchPostgresPassword);
    });
var researchDb = researchPostgres.AddDatabase(options.ResearchDatabaseResourceName, options.ResearchDatabaseName);

var researchRedis = builder.AddRedis(options.ResearchRedisResourceName, password: researchRedisPassword)
    .WithImageReference(options.ResearchRedisImage)
    .PublishAsDockerComposeService((_, service) =>
        SetServiceEnvironment(service, "REDIS_PASSWORD", options.ResearchRedisPassword));

var ollama = builder.AddContainer(options.OllamaResourceName, options.OllamaImage.Name, options.OllamaImage.Tag)
    .WithOptionalImageSHA256(options.OllamaImage.Sha256)
    .WithEnvironment("OLLAMA_HOST", string.Concat(options.OllamaBindHost, ":", options.OllamaPort))
    .WithVolume(options.OllamaDataVolume, options.OllamaDataPath)
    .WithHttpEndpoint(targetPort: options.OllamaPort, name: "http");

var ollamaInit = builder.AddContainer(options.OllamaInitResourceName, options.OllamaImage.Name, options.OllamaImage.Tag)
    .WithOptionalImageSHA256(options.OllamaImage.Sha256)
    .WithEnvironment("OLLAMA_HOST", ReferenceExpression.Create($"{ollama.GetEndpoint("http").Property(EndpointProperty.HostAndPort)}"))
    .WithArgs("pull", options.EmbeddingModelId)
    .WaitFor(ollama);

var chatEndpoint = ReferenceExpression.Create($"{options.ChatEndpoint}");
var firecrawlEndpoint = ReferenceExpression.Create($"{options.FirecrawlBaseUrl}");
ReferenceExpression? chatAliasTarget = null;
ReferenceExpression? firecrawlAliasTarget = null;

IResourceBuilder<ContainerResource>? firecrawl = null;
IResourceBuilder<ContainerResource>? vllm = null;

if (options.Profile == AppHostProfile.Full)
{
    var crawl = AddFirecrawlStack(builder, options);
    firecrawl = crawl.Firecrawl;
    firecrawlAliasTarget = ReferenceExpression.Create($"{crawl.Firecrawl.GetEndpoint("http")}");

    var model = AddVllm(builder, options);
    vllm = model;
    chatAliasTarget = ReferenceExpression.Create($"{model.GetEndpoint("http")}");
}

if (options.AppMode == AppExecutionMode.Images)
{
    var api = AddApiContainer(
        builder,
        options,
        researchDb,
        researchRedis,
        ollama,
        ollamaInit,
        chatEndpoint,
        firecrawlEndpoint,
        chatAliasTarget,
        firecrawlAliasTarget);
    AddOptionalFullProfileWaits(api, firecrawl, vllm);

    var webui = AddWebUiContainer(builder, options).WaitFor(api);
    var edge = AddEdge(
            builder,
            options,
            ReferenceExpression.Create($"{api.GetEndpoint("http")}"),
            ReferenceExpression.Create($"{webui.GetEndpoint("http")}"))
        .WaitFor(api)
        .WaitFor(webui);
    AddOptionalCertificateInstaller(builder, options, edge);
}
else
{
    var api = AddApiProject(
        builder,
        options,
        researchDb,
        researchRedis,
        ollama,
        ollamaInit,
        chatEndpoint,
        firecrawlEndpoint,
        chatAliasTarget,
        firecrawlAliasTarget);
    AddOptionalFullProfileWaits(api, firecrawl, vllm);

    var webui = AddWebUiProject(builder, options).WaitFor(api);
    var edge = AddEdge(
            builder,
            options,
            ReferenceExpression.Create($"{api.GetEndpoint("http")}"),
            ReferenceExpression.Create($"{webui.GetEndpoint("http")}"))
        .WaitFor(api)
        .WaitFor(webui);
    AddOptionalCertificateInstaller(builder, options, edge);
}

builder.AddDockerComposeEnvironment("compose")
    .WithDashboard(dashboard => dashboard.WithHostPort(18888))
    .ConfigureComposeFile(composeFile => composeFile.Name = options.ComposeProjectName)
    .ConfigureEnvFile(env => env.Clear());

builder.Build().Run();

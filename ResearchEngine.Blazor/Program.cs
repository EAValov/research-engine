using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using ResearchEngine.Blazor;
using ResearchEngine.Blazor.Api;
using ResearchEngine.Blazor.Services;
using ResearchEngine.Blazor.State;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddSingleton<AuthTokenProvider>();
builder.Services.AddTransient<AuthHeaderHandler>();

builder.Services.AddScoped<LocalStorageService>(); 
builder.Services.AddScoped<AppStateStore>();

builder.Services.AddScoped<EvidenceFacade>();
builder.Services.AddScoped<JobFacade>();
builder.Services.AddScoped<SynthesisFacade>();
builder.Services.AddScoped<LearningGroupFacade>();
builder.Services.AddScoped<SynthesisIterationFacade>();
builder.Services.AddScoped<SynthesisHistoryFacade>();
builder.Services.AddScoped<JobEventsClient>();
builder.Services.AddScoped<OverridesStore>();
builder.Services.AddScoped<ResearchProtocolFacade>();
builder.Services.AddScoped<JobCreationFacade>();

builder.Services.AddScoped(sp =>
{
    var apiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? "http://localhost:8090";
    var handler = sp.GetRequiredService<AuthHeaderHandler>();
    handler.InnerHandler = new HttpClientHandler();

    return new HttpClient(handler)
    {
        BaseAddress = new Uri(apiBaseUrl.TrimEnd('/') + "/")
    };
});

builder.Services.AddScoped<IResearchApiClient>(sp =>
{
    var http = sp.GetRequiredService<HttpClient>();
    return new ResearchApiClient(http);
});


await builder.Build().RunAsync();
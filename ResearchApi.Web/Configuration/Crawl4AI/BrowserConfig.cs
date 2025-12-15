using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ResearchApi.Configuration.Crawl4AI;

/// <summary>
/// Configuration for setting up a browser instance and its context in AsyncPlaywrightCrawlerStrategy.
/// Mirrors Crawl4AI's BrowserConfig.
/// </summary>
public sealed record BrowserConfig
{
    /// <summary>
    /// The type of browser to launch. Supported values: "chromium", "firefox", "webkit".
    /// Default: "chromium".
    /// </summary>
    [JsonPropertyName("browser_type")]
    public string BrowserType { get; set; } = "chromium";

    /// <summary>
    /// Whether to run the browser in headless mode (no visible GUI).
    /// Default: true.
    /// </summary>
    [JsonPropertyName("headless")]
    public bool Headless { get; set; } = true;

    /// <summary>
    /// Determines how the browser should be initialized:
    /// "builtin" (builtin CDP), "dedicated", "cdp", "docker".
    /// Default: "dedicated".
    /// </summary>
    [JsonPropertyName("browser_mode")]
    public string BrowserMode { get; set; } = "dedicated";

    /// <summary>
    /// Launch the browser using a managed approach (e.g., via CDP).
    /// Default: false.
    /// </summary>
    [JsonPropertyName("use_managed_browser")]
    public bool UseManagedBrowser { get; set; } = false;

    /// <summary>
    /// URL for the Chrome DevTools Protocol endpoint (CDP).
    /// Default: "ws://localhost:9222/devtools/browser/".
    /// </summary>
    [JsonPropertyName("cdp_url")]
    public string? CdpUrl { get; set; }

    /// <summary>
    /// Port for the browser debugging protocol.
    /// Default: 9222.
    /// </summary>
    [JsonPropertyName("debugging_port")]
    public int DebuggingPort { get; set; } = 9222;

    /// <summary>
    /// Use a persistent browser context (like a persistent profile).
    /// Automatically sets use_managed_browser = true.
    /// Default: false.
    /// </summary>
    [JsonPropertyName("use_persistent_context")]
    public bool UsePersistentContext { get; set; } = false;

    /// <summary>
    /// Path to a user data directory for persistent sessions.
    /// Default: null.
    /// </summary>
    [JsonPropertyName("user_data_dir")]
    public string? UserDataDir { get; set; }

    /// <summary>
    /// Chrome channel to launch (e.g., "chrome", "msedge"). Applies to chromium.
    /// Default: "chromium".
    /// </summary>
    [JsonPropertyName("chrome_channel")]
    public string ChromeChannel { get; set; } = "chromium";

    /// <summary>
    /// Channel to launch (e.g., "chromium", "chrome", "msedge").
    /// Default: "chromium".
    /// </summary>
    [JsonPropertyName("channel")]
    public string Channel { get; set; } = "chromium";

    /// <summary>
    /// Proxy server URL (e.g. "http://user:pass@proxy:port").
    /// Default: null.
    /// </summary>
    [JsonPropertyName("proxy")]
    public string? Proxy { get; set; }

    /// <summary>
    /// Detailed proxy configuration (e.g. { "server": "...", "username": "..." }).
    /// Default: null.
    /// </summary>
    [JsonPropertyName("proxy_config")]
    public object? ProxyConfig { get; set; }

    /// <summary>
    /// Default viewport width. Default: 1080.
    /// </summary>
    [JsonPropertyName("viewport_width")]
    public int ViewportWidth { get; set; } = 1080;

    /// <summary>
    /// Default viewport height. Default: 600.
    /// </summary>
    [JsonPropertyName("viewport_height")]
    public int ViewportHeight { get; set; } = 600;

    /// <summary>
    /// Optional viewport dictionary. If set, overrides viewport_width/viewport_height.
    /// </summary>
    [JsonPropertyName("viewport")]
    public Dictionary<string, int>? Viewport { get; set; }

    /// <summary>
    /// Enable verbose logging. Default: true.
    /// </summary>
    [JsonPropertyName("verbose")]
    public bool Verbose { get; set; } = true;

    /// <summary>
    /// Whether to allow file downloads (requires downloads_path). Default: false.
    /// </summary>
    [JsonPropertyName("accept_downloads")]
    public bool AcceptDownloads { get; set; } = false;

    /// <summary>
    /// Directory to store downloaded files. Default: null.
    /// </summary>
    [JsonPropertyName("downloads_path")]
    public string? DownloadsPath { get; set; }

    /// <summary>
    /// In-memory storage state (cookies, localStorage). Default: null.
    /// </summary>
    [JsonPropertyName("storage_state")]
    public object? StorageState { get; set; }

    /// <summary>
    /// Ignore HTTPS certificate errors. Default: true.
    /// </summary>
    [JsonPropertyName("ignore_https_errors")]
    public bool IgnoreHttpsErrors { get; set; } = true;

    /// <summary>
    /// Enable JavaScript execution in pages. Default: true.
    /// </summary>
    [JsonPropertyName("java_script_enabled")]
    public bool JavaScriptEnabled { get; set; } = true;

    /// <summary>
    /// List of cookies for the browser context.
    /// Each cookie: { "name": "...", "value": "...", "url": "..." }.
    /// </summary>
    [JsonPropertyName("cookies")]
    public List<Dictionary<string, object>>? Cookies { get; set; }

    /// <summary>
    /// Extra HTTP headers applied to all requests in this context.
    /// </summary>
    [JsonPropertyName("headers")]
    public Dictionary<string, string>? Headers { get; set; }

    /// <summary>
    /// Custom User-Agent string.
    /// </summary>
    [JsonPropertyName("user_agent")]
    public string? UserAgent { get; set; }

    /// <summary>
    /// Mode for generating user agent (e.g. "random") or null to use provided user_agent.
    /// </summary>
    [JsonPropertyName("user_agent_mode")]
    public string? UserAgentMode { get; set; }

    /// <summary>
    /// Configuration for user agent generation if user_agent_mode is set. Default: null.
    /// </summary>
    [JsonPropertyName("user_agent_generator_config")]
    public Dictionary<string, object>? UserAgentGeneratorConfig { get; set; }

    /// <summary>
    /// If true, disables images and rich content for faster load times. Default: false.
    /// </summary>
    [JsonPropertyName("text_mode")]
    public bool TextMode { get; set; } = false;

    /// <summary>
    /// Disables certain background features for performance gains. Default: false.
    /// </summary>
    [JsonPropertyName("light_mode")]
    public bool LightMode { get; set; } = false;

    /// <summary>
    /// Additional command-line arguments passed to the browser. Default: [].
    /// </summary>
    [JsonPropertyName("extra_args")]
    public List<string>? ExtraArgs { get; set; }

    /// <summary>
    /// If true, applies playwright-stealth to bypass basic bot detection.
    /// Default: false.
    /// </summary>
    [JsonPropertyName("enable_stealth")]
    public bool EnableStealth { get; set; } = false;
}
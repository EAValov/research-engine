using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ResearchApi.Configuration.Crawl4AI;

/// <summary>
/// Configuration controlling how the crawler runs each operation.
/// Mirrors Crawl4AI's CrawlerRunConfig.
/// </summary>
public sealed record CrawlerRunConfig
{
    // ---------- Content Processing ----------

    /// <summary>
    /// Minimum word count threshold before processing content.
    /// Default: ~200.
    /// </summary>
    [JsonPropertyName("word_count_threshold")]
    public int? WordCountThreshold { get; set; }

    /// <summary>
    /// Extraction strategy to extract structured data (Python-side strategy name or config).
    /// Default: None.
    /// </summary>
    [JsonPropertyName("extraction_strategy")]
    public object? ExtractionStrategy { get; set; }

    /// <summary>
    /// Chunking strategy for content before extraction.
    /// Default: RegexChunking.
    /// </summary>
    [JsonPropertyName("chunking_strategy")]
    public object? ChunkingStrategy { get; set; }

    /// <summary>
    /// Markdown generation strategy. Default: internal default.
    /// </summary>
    [JsonPropertyName("markdown_generator")]
    public object? MarkdownGenerator { get; set; }

    /// <summary>
    /// If true, attempt to extract text-only content where applicable.
    /// Default: false.
    /// </summary>
    [JsonPropertyName("only_text")]
    public bool? OnlyText { get; set; }

    /// <summary>
    /// CSS selector to extract a specific portion of the page. Default: null.
    /// </summary>
    [JsonPropertyName("css_selector")]
    public string? CssSelector { get; set; }

    /// <summary>
    /// List of CSS selectors for specific elements for Markdown/extraction.
    /// Only these elements are processed if set.
    /// </summary>
    [JsonPropertyName("target_elements")]
    public List<string>? TargetElements { get; set; }

    /// <summary>
    /// List of HTML tags to exclude from processing. Default: null.
    /// </summary>
    [JsonPropertyName("excluded_tags")]
    public List<string>? ExcludedTags { get; set; }

    /// <summary>
    /// CSS selector to exclude from processing. Default: null.
    /// </summary>
    [JsonPropertyName("excluded_selector")]
    public string? ExcludedSelector { get; set; }

    /// <summary>
    /// If true, retain data-* attributes when cleaning HTML. Default: false.
    /// </summary>
    [JsonPropertyName("keep_data_attributes")]
    public bool? KeepDataAttributes { get; set; }

    /// <summary>
    /// List of attributes to keep when cleaning HTML.
    /// </summary>
    [JsonPropertyName("keep_attrs")]
    public List<string>? KeepAttrs { get; set; }

    /// <summary>
    /// If true, remove all &lt;form&gt; elements. Default: false.
    /// </summary>
    [JsonPropertyName("remove_forms")]
    public bool? RemoveForms { get; set; }

    /// <summary>
    /// If true, prettify HTML output. Default: false.
    /// </summary>
    [JsonPropertyName("prettiify")]
    public bool? Prettiify { get; set; }

    /// <summary>
    /// Type of parser to use for HTML parsing. Default: "lxml".
    /// </summary>
    [JsonPropertyName("parser_type")]
    public string? ParserType { get; set; }

    /// <summary>
    /// Scraping strategy to use (e.g. LXMLWebScrapingStrategy).
    /// </summary>
    [JsonPropertyName("scraping_strategy")]
    public object? ScrapingStrategy { get; set; }

    /// <summary>
    /// Detailed proxy configuration for requests. Default: null.
    /// </summary>
    [JsonPropertyName("proxy_config")]
    public object? ProxyConfig { get; set; }

    // ---------- Locale / Location ----------

    /// <summary>
    /// Locale for the browser context (e.g. "en-US"). Default: null.
    /// </summary>
    [JsonPropertyName("locale")]
    public string? Locale { get; set; }

    /// <summary>
    /// Timezone identifier for the browser context (e.g. "Europe/Berlin"). Default: null.
    /// </summary>
    [JsonPropertyName("timezone_id")]
    public string? TimezoneId { get; set; }

    /// <summary>
    /// Geolocation configuration. Default: null.
    /// </summary>
    [JsonPropertyName("geolocation")]
    public object? Geolocation { get; set; }

    // ---------- Caching ----------

    /// <summary>
    /// Cache mode configuration ("CacheMode" typed config in Docker API).
    /// Typically: { "type": "CacheMode", "params": "bypass" }.
    /// </summary>
    [JsonPropertyName("cache_mode")]
    public TypedConfig<string>? CacheMode { get; set; }

    /// <summary>
    /// Optional session ID to persist browser context and page instance.
    /// </summary>
    [JsonPropertyName("session_id")]
    public string? SessionId { get; set; }

    // ---------- Navigation / Timing ----------

    /// <summary>
    /// Condition to wait for when navigating (e.g. "domcontentloaded", "networkidle").
    /// Default: "domcontentloaded".
    /// </summary>
    [JsonPropertyName("wait_until")]
    public string? WaitUntil { get; set; }

    /// <summary>
    /// Timeout in ms for page operations. Default: 60000.
    /// </summary>
    [JsonPropertyName("page_timeout")]
    public int? PageTimeout { get; set; }

    /// <summary>
    /// CSS selector or JS condition to wait for before extraction. Default: null.
    /// </summary>
    [JsonPropertyName("wait_for")]
    public string? WaitFor { get; set; }

    /// <summary>
    /// Specific timeout in ms for wait_for condition. Default: null (uses page_timeout).
    /// </summary>
    [JsonPropertyName("wait_for_timeout")]
    public int? WaitForTimeout { get; set; }

    /// <summary>
    /// If true, wait for images to load before extraction. Default: false.
    /// </summary>
    [JsonPropertyName("wait_for_images")]
    public bool? WaitForImages { get; set; }

    /// <summary>
    /// Delay in seconds before retrieving final HTML. Default: 0.1.
    /// </summary>
    [JsonPropertyName("delay_before_return_html")]
    public double? DelayBeforeReturnHtml { get; set; }

    // ---------- Interaction / Anti-bot ----------

    /// <summary>
    /// JavaScript code/snippets to run on the page. Default: null.
    /// </summary>
    [JsonPropertyName("js_code")]
    public object? JsCode { get; set; }

    /// <summary>
    /// If true, indicates subsequent calls are JS-driven updates, not full page loads.
    /// Default: false.
    /// </summary>
    [JsonPropertyName("js_only")]
    public bool? JsOnly { get; set; }

    /// <summary>
    /// If true, scroll through entire page to load all content. Default: false.
    /// </summary>
    [JsonPropertyName("scan_full_page")]
    public bool? ScanFullPage { get; set; }

    /// <summary>
    /// If true, attempts to process & inline iframe content. Default: false.
    /// </summary>
    [JsonPropertyName("process_iframes")]
    public bool? ProcessIframes { get; set; }

    /// <summary>
    /// If true, remove overlays/popups before extracting HTML. Default: false.
    /// </summary>
    [JsonPropertyName("remove_overlay_elements")]
    public bool? RemoveOverlayElements { get; set; }

    /// <summary>
    /// If true, simulate user interactions (mouse moves, clicks) for anti-bot measures.
    /// Default: false.
    /// </summary>
    [JsonPropertyName("simulate_user")]
    public bool? SimulateUser { get; set; }

    /// <summary>
    /// If true, override navigator properties to look more human. Default: false.
    /// </summary>
    [JsonPropertyName("override_navigator")]
    public bool? OverrideNavigator { get; set; }

    /// <summary>
    /// If true, attempt automatic handling of overlays/popups. Default: false.
    /// </summary>
    [JsonPropertyName("magic")]
    public bool? Magic { get; set; }

    // ---------- Media ----------

    /// <summary>
    /// Whether to take a screenshot. Default: false.
    /// </summary>
    [JsonPropertyName("screenshot")]
    public bool? Screenshot { get; set; }

    /// <summary>
    /// Whether to generate a PDF of the page. Default: false.
    /// </summary>
    [JsonPropertyName("pdf")]
    public bool? Pdf { get; set; }

    // ---------- Link/Domain filters (shortened) ----------

    [JsonPropertyName("exclude_external_links")]
    public bool? ExcludeExternalLinks { get; set; }

    [JsonPropertyName("exclude_internal_links")]
    public bool? ExcludeInternalLinks { get; set; }

    [JsonPropertyName("exclude_social_media_links")]
    public bool? ExcludeSocialMediaLinks { get; set; }

    // ---------- Connection / streaming ----------

    /// <summary>
    /// If true, enables streaming of crawled URLs as they are processed (arun_many).
    /// For sync REST use: false.
    /// </summary>
    [JsonPropertyName("stream")]
    public bool? Stream { get; set; } = false;

    /// <summary>
    /// Whether to check robots.txt before crawling. Default: false.
    /// </summary>
    [JsonPropertyName("check_robots_txt")]
    public bool? CheckRobotsTxt { get; set; }

    /// <summary>
    /// Custom User-Agent override at the crawler level.
    /// </summary>
    [JsonPropertyName("user_agent")]
    public string? UserAgent { get; set; }

    /// <summary>
    /// Experimental parameters dictionary. Default: null.
    /// </summary>
    [JsonPropertyName("experimental")]
    public Dictionary<string, object>? Experimental { get; set; }
}

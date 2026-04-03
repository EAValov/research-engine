# Configuration Guide

Use this page in two ways:

- for first-time setup, read `Quick Mental Model`, `Most Common Settings`, and `Precedence`
- for detailed tuning, jump to the section reference further down

This guide is based on:

- `ResearchEngine.API`
- `ResearchEngine.WebUI`
- `ResearchEngine.API/appsettings.json`
- `ResearchEngine.WebUI/wwwroot/appsettings.json`
- the single-host deployment manifests in `Deploy/single-host`

## Quick Mental Model

- API startup configuration comes from `ResearchEngine.API/appsettings.json` and environment variables
- after the first startup, `ResearchOrchestratorConfig`, `LearningSimilarityOptions`, and `ChatConfig` become database-backed runtime settings stored in PostgreSQL
- Web UI startup defaults come from `ResearchEngine.WebUI/wwwroot/appsettings.json`
- each browser can override the Web UI API URL and API key locally through the settings dialog

## Most Common Settings

| Setting | What it controls | Usually set via |
| --- | --- | --- |
| `ConnectionStrings__ResearchDb` | main PostgreSQL connection | env var or secret |
| `FirecrawlOptions__BaseUrl` | search and scrape backend | env var |
| `ChatConfig__Endpoint`, `ChatConfig__ModelId`, `ChatConfig__ApiKey` | chat backend for planning and synthesis | env var initially, then the runtime settings UI |
| `EmbeddingConfig__Endpoint`, `EmbeddingConfig__ModelId`, `EmbeddingConfig__Dimension`, `EmbeddingConfig__ApiKey` | embeddings backend | env var |
| `ResearchOrchestratorConfig__DefaultDiscoveryMode` | default source-discovery policy preselected in the composer, including `Auto` | bootstrap config initially, then the runtime settings UI |
| `AuthenticationOptions__ApiKeys__0` | API access key | secret |
| `API_BASE_URL` | default API URL used by the Web UI container | env var |

## Where Settings Live

### API

The API uses standard ASP.NET Core configuration providers.

In practice, values come from:

- `ResearchEngine.API/appsettings.json`
- environment variables

On first startup, the API also seeds the PostgreSQL `runtime_settings` row for:

- `ResearchOrchestratorConfig`
- `LearningSimilarityOptions`
- `ChatConfig`

After that, those three sections are read from the database instead of from `appsettings.json`.

Environment variables use the standard `__` separator, for example:

```text
ConnectionStrings__ResearchDb
ChatConfig__Endpoint
EmbeddingConfig__Dimension
```

### Web UI

The Web UI is a Blazor WebAssembly app with two layers:

- startup defaults from `ResearchEngine.WebUI/wwwroot/appsettings.json`
- browser-local overrides stored by the settings dialog

In the containerized deployment, `ResearchEngine.WebUI/entrypoint.sh` rewrites the runtime `appsettings.json` before Caddy starts.

## Precedence

### API

1. Environment variables override `appsettings.json`.
2. On first startup, `ResearchOrchestratorConfig`, `LearningSimilarityOptions`, and `ChatConfig` are copied into the PostgreSQL `runtime_settings` row.
3. After that, those three sections are read from PostgreSQL by the app and by the runtime settings UI.
4. Other sections, including `EmbeddingConfig`, continue to use normal ASP.NET Core precedence.

Important consequence:

- if you change `ChatConfig__*`, `ResearchOrchestratorConfig__*`, or `LearningSimilarityOptions__*` in environment variables after first startup, the existing database row still wins until you update the runtime settings or reset that row

In the current `Deploy/single-host` manifests:

- `ResearchOrchestratorConfig` may be present in config as a bootstrap default
- `LearningSimilarityOptions` may be present in config as a bootstrap default
- `ChatConfig` may be present in config as a bootstrap default
- `EmbeddingConfig` is overridden by env vars
- several other API settings are overridden by env vars as well

### Web UI

1. deployment-generated `appsettings.json` provides defaults
2. browser-local saved settings override those defaults for that user

So the API URL and API key shown in the UI are user-specific, even if the container started with shared defaults.

## Common Gotchas

- `EmbeddingConfig__Dimension` must match the real embedding vector size exactly
- `ChatConfig__ApiKey` cannot be empty, even for local backends that ignore authentication
- after first startup, runtime-edited `ChatConfig`, `ResearchOrchestratorConfig`, and `LearningSimilarityOptions` live in PostgreSQL, not in `appsettings.json`
- Web UI API settings are stored per browser, so two users can see different API URLs or API keys

## API Configuration Reference

The most commonly touched API sections are:

- `ConnectionStrings`
- `FirecrawlOptions`
- `ChatConfig`
- `EmbeddingConfig`
- `ResearchOrchestratorConfig`
- `LearningSimilarityOptions`

| Section | Runtime-editable after first startup? | What it controls |
| --- | --- | --- |
| `ConnectionStrings` | No | PostgreSQL connections for the app and Hangfire |
| `FirecrawlOptions` | No | search and scraping backend |
| `ChatConfig` | Yes | chat backend used for planning and synthesis |
| `EmbeddingConfig` | No | embeddings backend and vector size |
| `ResearchOrchestratorConfig` | Yes | search breadth, crawl parallelism, and default source-discovery policy |
| `LearningSimilarityOptions` | Yes | evidence extraction, grouping, and retrieval heuristics |

### `ConnectionStrings`

```json
"ConnectionStrings": {
  "ResearchDb": "...",
  "HangfireDb": "..."
}
```

- `ResearchDb` is required
- `HangfireDb` is optional; if it is missing, the API falls back to `ResearchDb`

Usage:

- `ResearchDb`
  - main PostgreSQL database for the application
  - EF Core migrations target
  - database health check target
- `HangfireDb`
  - Hangfire job storage database

Notes:

- PostgreSQL must support the `vector` extension
- Migrations are applied automatically on startup

Single-host deployment example:

```yaml
env:
  - name: ConnectionStrings__ResearchDb
    valueFrom:
      secretKeyRef:
        name: research-single-host-secrets
        key: RESEARCH_CONNECTION_STRING
  - name: ConnectionStrings__HangfireDb
    valueFrom:
      secretKeyRef:
        name: research-single-host-secrets
        key: RESEARCH_CONNECTION_STRING
```

### `FirecrawlOptions`

```json
"FirecrawlOptions": {
  "BaseUrl": "http://firecrawl:3002",
  "ApiKey": "...",
  "HttpClientTimeoutSeconds": 600
}
```

- `BaseUrl` is the Firecrawl service base URL
- `ApiKey` is optional in the type, but typically required in real deployments
- `HttpClientTimeoutSeconds` controls the timeout for search and scrape requests

Single-host deployment example:

```yaml
env:
  - name: FirecrawlOptions__BaseUrl
    value: "http://research-crawl:3002"
  - name: FirecrawlOptions__ApiKey
    valueFrom:
      secretKeyRef:
        name: research-single-host-secrets
        key: FIRE_CRAWL_API_KEY
  - name: FirecrawlOptions__HttpClientTimeoutSeconds
    value: "600"
```

### `ChatConfig`

```json
"ChatConfig": {
  "Endpoint": "http://vllm:8000/v1",
  "ApiKey": "...",
  "ModelId": "nvidia/Qwen3-30B-A3B-NVFP4",
  "MaxContextLength": 32768
}
```

- `Endpoint` is the OpenAI-compatible chat backend
- `ApiKey` is required by the current implementation
- `ModelId` is required
- `MaxContextLength` is optional

Usage:

- research planning and synthesis chat calls
- `/health/ready` chat backend health check via `{Endpoint}/models`
- tokenizer calls against the same backend family

Important:

- if `MaxContextLength` is provided, the app uses a heuristic token estimate with a 20% safety buffer and does not require a `/tokenize` endpoint
- if `MaxContextLength` is not provided, the app uses the chat backend's `/tokenize` endpoint
- `MaxContextLength` must be at least `10000`; smaller context windows degrade quality a lot
- `MaxContextLength` is validated on startup when provided; other bad `ChatConfig` values may still fail when first used
- the current implementation requires a non-empty `ApiKey` value even for local backends that ignore authentication; use a dummy value such as `ollama` if needed
- the current sample setup is tuned for a single RTX 5090 and using the `nvidia/Qwen3-30B-A3B-NVFP4` model.

Single-host deployment example:

```yaml
env:
  - name: ChatConfig__Endpoint
    value: "http://research-llm:8000/v1"
  - name: ChatConfig__ModelId
    value: "nvidia/Qwen3-30B-A3B-NVFP4"
  - name: ChatConfig__ApiKey
    valueFrom:
      secretKeyRef:
        name: research-single-host-secrets
        key: ChatConfig__ApiKey
```

Optional override for backends that do not expose `/tokenize`:

```yaml
env:
  - name: ChatConfig__MaxContextLength
    value: "32768"
```

#### Chat Backend Requirements

The current chat backend must support these features for full compatibility:

- `POST /v1/chat/completions`
- `GET /v1/models`
- OpenAI-style structured output using `response_format` with a JSON schema
- OpenAI-style tool calling for synthesis section generation, including specific-function tool choice
- either:
  - a `/tokenize` endpoint, or
  - `ChatConfig__MaxContextLength` set to a realistic context window value

These requirements come from the current code paths:

- structured outputs are used in `ResearchProtocolService`, `QueryPlanningService`, `LearningIntelService`, and section planning in `ReportSynthesisService`
- tool calling is used during section writing in `ReportSynthesisService`, and the current implementation requires a specific named tool
- `/models` is used by health checks and the runtime settings UI
- `/tokenize` is used by the tokenizer unless `MaxContextLength` is configured

### `EmbeddingConfig`

```json
"EmbeddingConfig": {
  "Endpoint": "http://ollama:11434/v1",
  "ApiKey": "...",
  "ModelId": "Qwen/Qwen3-Embedding-0.6B",
  "Dimension": 1024
}
```

- `Endpoint` is the OpenAI-compatible embeddings backend
- `ApiKey` is required by the current implementation
- `ModelId` is required
- `Dimension` must match the real embedding vector size

Usage:

- learning embeddings
- learning-group embeddings
- pgvector column dimensions in the EF model
- `/health/ready` embedding backend health check via `{Endpoint}/models`

Important:

- if `Dimension` does not match the actual vector length, persistence and vector search will break
- The example config uses the `Qwen/Qwen3-Embedding-0.6B` embedding model.

Single-host deployment example:

```yaml
env:
  - name: EmbeddingConfig__Endpoint
    value: "http://127.0.0.1:11434/v1"
  - name: EmbeddingConfig__ModelId
    value: "qwen3-embedding:0.6b"
  - name: EmbeddingConfig__Dimension
    value: "1024"
  - name: EmbeddingConfig__ApiKey
    valueFrom:
      secretKeyRef:
        name: research-single-host-secrets
        key: EmbeddingConfig__ApiKey
```

### `ResearchOrchestratorConfig`

```json
"ResearchOrchestratorConfig": {
  "LimitSearches": 5,
  "MaxUrlParallelism": 1,
  "MaxUrlsPerSerpQuery": 20,
  "DefaultDiscoveryMode": "Auto"
}
```

This section is validated on startup.

Current validation:

- `LimitSearches`: `1..1000`
- `MaxUrlParallelism`: `1..1000`
- `MaxUrlsPerSerpQuery`: `1..1000`
- `DefaultDiscoveryMode`: one of `Auto`, `Balanced`, `ReliableOnly`, `AcademicOnly`

Meaning:

- `LimitSearches`
  - maximum number of search results requested for each SERP query
- `MaxUrlParallelism`
  - maximum number of source URLs processed in parallel for one SERP query
- `MaxUrlsPerSerpQuery`
  - maximum number of unique URLs processed from a SERP query
- `DefaultDiscoveryMode`
  - default source-discovery mode used to preselect the Web UI composer and to seed jobs that do not specify a per-job override

Discovery modes:

- `Auto`
  - asks the protocol layer to choose `Balanced`, `ReliableOnly`, or `AcademicOnly` per query
- `Balanced`
  - general-purpose discovery that mixes broad web search with deterministic source-quality scoring
- `ReliableOnly`
  - filters discovery toward higher-trust sources such as official, government, academic, journal, and established publication domains
- `AcademicOnly`
  - restricts discovery toward research-oriented sources such as academic domains, journals, and preprints

Source trust rules:

- trust rules are currently code-defined in `ResearchEngine.API/Infrastructure/SourceTrustRuleCatalog.cs`
- the global pack is always active
- regional packs are activated automatically when the job language or human-readable region string matches a known pack
- the built-in regional packs currently include `Russia` and `China`
- trust rules are not stored in PostgreSQL and are not currently editable through the Settings dialog

These settings are loaded from the shared runtime-settings row in PostgreSQL and are exposed in the Web UI settings dialog.

In the current `Deploy/single-host` deployment, this section is not overridden by environment variables, so UI changes remain effective.

### `LearningSimilarityOptions`

```json
"LearningSimilarityOptions": {
  "MinImportance": 0.65,
  "DiversityMaxPerUrl": 3,
  "DiversityMaxTextSimilarity": 0.85,
  "MaxLearningsPerSegment": 20,
  "MinLearningsPerSegment": 5,
  "GroupAssignSimilarityThreshold": 0.93,
  "GroupSearchTopK": 5,
  "MaxEvidenceLength": 20000
}
```

This section is validated on startup and also validated by the runtime settings endpoint.

Current validation:

- `MinImportance`: `0.0..1.0`
- `DiversityMaxPerUrl`: `1..1000`
- `DiversityMaxTextSimilarity`: `0.0..1.0`
- `MaxLearningsPerSegment`: `1..1000`
- `MinLearningsPerSegment`: `1..1000`
- `GroupAssignSimilarityThreshold`: `0.0..1.0`
- `GroupSearchTopK`: `1..50`
- `MaxEvidenceLength`: `1..1_000_000`
- cross-property rule: `MinLearningsPerSegment <= MaxLearningsPerSegment`

Meaning:

- `MinImportance`
  - minimum importance score used when searching for similar learnings
- `DiversityMaxPerUrl`
  - maximum number of returned learnings from the same source URL
- `DiversityMaxTextSimilarity`
  - maximum allowed Jaccard similarity between learning texts during diversity filtering
- `MaxLearningsPerSegment`
  - upper bound for learnings extracted from a content segment
- `MinLearningsPerSegment`
  - lower bound for learnings extracted from a content segment
- `GroupAssignSimilarityThreshold`
  - minimum cosine similarity required to auto-assign a learning to an existing group
- `GroupSearchTopK`
  - number of nearest learning groups inspected during grouping
- `MaxEvidenceLength`
  - maximum stored evidence length in characters

These settings are loaded from the shared runtime-settings row in PostgreSQL and are exposed in the Web UI settings dialog.

In the current `Deploy/single-host` deployment, this section is not overridden by environment variables, so UI changes remain effective.

<details>
<summary>Advanced API sections: authentication, Redis, Hangfire, CORS, rate limiting, and logging</summary>

### `AuthenticationOptions`

```json
"AuthenticationOptions": {
  "Enabled": true,
  "ApiKeys": [
    "api-key-1"
  ]
}
```

- `Enabled`
  - when `false`, the API key authentication handler returns `NoResult`
- `ApiKeys`
  - allowed API keys for API access

Notes:

- most `/api/*` endpoints require authorization
- the anonymous SSE stream still depends on the ticket flow created by the API
- API keys should be supplied through secrets or environment variables, not committed JSON
- requests still use the standard `Authorization: Bearer <api-key>` header format

Single-host deployment example:

```yaml
env:
  - name: AuthenticationOptions__Enabled
    value: "true"
  - name: AuthenticationOptions__ApiKeys__0
    valueFrom:
      secretKeyRef:
        name: research-single-host-secrets
        key: AuthenticationOptions__ApiKeys__0
```

### `RedisEventBusOptions`

```json
"RedisEventBusOptions": {
  "ConnectionString": "redis:6379"
}
```

This section is validated on startup.

Current validation:

- `ConnectionString` is required

Usage:

- research event bus
- Redis health check
- distributed cache for IP rate limiting when enabled

Single-host deployment example:

```yaml
env:
  - name: RedisEventBusOptions__ConnectionString
    value: "127.0.0.1:6379"
```

### `Hangfire`

This section is optional and is not present in the sample `appsettings.json`, but the code supports it.

Supported keys:

```json
"Hangfire": {
  "EnableServer": true,
  "WorkerCount": 2,
  "QueuePollMs": 200
}
```

- `EnableServer`
  - default `true`
- `WorkerCount`
  - default `2`
- `QueuePollMs`
  - optional queue polling interval in milliseconds

Queues used by the application:

- `jobs`
- `synthesis`

### `Cors`

```json
"Cors": {
  "AllowedOrigins": [
    "http://localhost:5170"
  ]
}
```

`AllowedOrigins` defines the `WebUIDev` CORS policy.

If the section is missing or empty, the API falls back to these built-in defaults:

- `http://localhost:5170`
- `http://127.0.0.1:5170`
- `http://localhost:5173`
- `http://127.0.0.1:5173`
- `https://localhost:5001`
- `http://localhost:5000`

Single-host deployment example:

```yaml
env:
  - name: Cors__AllowedOrigins__0
    value: "http://localhost:8080"
  - name: Cors__AllowedOrigins__1
    value: "http://127.0.0.1:8080"
  - name: Cors__AllowedOrigins__2
    value: "https://research-webui.llm.local:8443"
```

### `IpRateLimiting`

The app enables IP rate limiting only when both of these are true:

- `IpRateLimiting:Enabled=true`
- `IpRateLimiting:GeneralRules` contains at least one rule

Example shape:

```json
"IpRateLimiting": {
  "Enabled": true,
  "EnableEndpointRateLimiting": true,
  "StackBlockedRequests": false,
  "HttpStatusCode": 429,
  "RealIpHeader": "X-Forwarded-For",
  "ClientIdHeader": "X-ClientId",
  "DisableRateLimitHeaders": false,
  "GeneralRules": [
    { "Endpoint": "*:/api/*", "Period": "1m", "Limit": 600 }
  ]
}
```

Related section:

```json
"IpRateLimitPolicies": {
  "IpRules": []
}
```

Notes:

- Redis stores counters and policies
- when rate limiting is enabled, forwarded headers are trusted and `X-Forwarded-For` is used

### `Serilog`

Logging is configured through the `Serilog` section.

The current sample configuration writes logs to:

- console
- `logs/app.log` with hourly rolling files

</details>

## API Runtime Settings Endpoint

The API exposes a runtime settings endpoint:

- `GET /api/settings/runtime`
- `PUT /api/settings/runtime`

It currently supports:

- `ResearchOrchestratorConfig`
- `LearningSimilarityOptions`
- `ChatConfig`
- read-only model information from `EmbeddingConfig`

Behavior:

- `GET` reads the shared `runtime_settings` row from PostgreSQL
- `PUT` updates the shared `runtime_settings` row in PostgreSQL
- API instances load the current runtime settings from the database per request / job scope
- the row is created automatically from config defaults on first startup after migrations

Current live-update behavior:

- `ResearchOrchestratorConfig`: live
- `LearningSimilarityOptions`: live
- `ChatConfig`: live
- `EmbeddingConfig`: displayed, not updated by the endpoint

Important:

- runtime settings updates are shared across API instances that use the same database
- `EmbeddingConfig` and other non-runtime settings still come from normal configuration sources

## Web UI Configuration

### Startup Defaults

The Web UI default config file is:

```json
{
  "ApiBaseUrl": "http://localhost:8090",
  "ApiAuth": {
    "ApiKey": ""
  }
}
```

- `ApiBaseUrl`
  - default API base URL for browser requests
- `ApiAuth:ApiKey`
  - default API key

### Browser-Saved Settings

The Web UI settings dialog allows the user to change:

- API base URL
- API auth enabled/disabled
- API key

Those values are stored in browser local storage and override the startup defaults for that user.

The same dialog also loads and updates the API runtime settings described above. That includes `ResearchOrchestratorConfig.DefaultDiscoveryMode`, which is shared through PostgreSQL rather than stored in browser local storage. In the research composer, the `Sources` selector is prepopulated from that current default, and the user can still override it per job.

### Web UI Container Mapping

In the single-host deployment, the Web UI container receives:

```yaml
env:
  - name: API_BASE_URL
    value: "http://localhost:8080"
  - name: AuthenticationOptions__ApiKeys__0
    valueFrom:
      secretKeyRef:
        name: research-single-host-secrets
        key: AuthenticationOptions__ApiKeys__0
```

`entrypoint.sh` maps those to:

- `API_BASE_URL` -> `ApiBaseUrl`
- `AuthenticationOptions__ApiKeys__0` -> `ApiAuth:ApiKey`

These are only startup defaults. The browser can still override them later.

## Single-Host Deployment Summary

The current `Deploy/single-host` manifests configure these important runtime values through environment variables:

- database connection strings
- Firecrawl base URL and API key
- embedding endpoint, model id, dimension, and API key
- Redis connection string
- API key auth enabled flag and API key
- Web UI default API URL

The same manifests may still contain bootstrap defaults for:

- `ChatConfig`
- `ResearchOrchestratorConfig`
- `LearningSimilarityOptions`

Those values are copied into the database only when the `runtime_settings` row does not exist yet.

## Secrets

Treat these values as secrets and inject them outside source control:

- `ConnectionStrings__ResearchDb`
- `ConnectionStrings__HangfireDb`
- `FirecrawlOptions__ApiKey`
- `ChatConfig__ApiKey`
- `EmbeddingConfig__ApiKey`
- `AuthenticationOptions__ApiKeys__*`

The single-host example stores them as Kubernetes-style `Secret` manifests in `Deploy/single-host/20-app.yaml` and `Deploy/single-host/30-crawl.yaml`.

## Minimal Environment Variable Example

<details>
<summary>Open minimal API and Web UI env var examples</summary>

API example:

```text
ConnectionStrings__ResearchDb=Host=localhost;Port=5432;Database=research;Username=app;Password=secret
ConnectionStrings__HangfireDb=Host=localhost;Port=5432;Database=jobs;Username=app;Password=secret
FirecrawlOptions__BaseUrl=http://localhost:3002
FirecrawlOptions__ApiKey=your-firecrawl-key
FirecrawlOptions__HttpClientTimeoutSeconds=600
ChatConfig__Endpoint=http://localhost:8000/v1
ChatConfig__ApiKey=your-chat-key
ChatConfig__ModelId=your-chat-model
ChatConfig__MaxContextLength=32768
EmbeddingConfig__Endpoint=http://localhost:11434/v1
EmbeddingConfig__ApiKey=your-embedding-key
EmbeddingConfig__ModelId=your-embedding-model
EmbeddingConfig__Dimension=1024
ResearchOrchestratorConfig__LimitSearches=5
ResearchOrchestratorConfig__MaxUrlParallelism=1
ResearchOrchestratorConfig__MaxUrlsPerSerpQuery=20
ResearchOrchestratorConfig__DefaultDiscoveryMode=Balanced
LearningSimilarityOptions__MinImportance=0.65
LearningSimilarityOptions__DiversityMaxPerUrl=3
LearningSimilarityOptions__DiversityMaxTextSimilarity=0.85
LearningSimilarityOptions__MaxLearningsPerSegment=20
LearningSimilarityOptions__MinLearningsPerSegment=5
LearningSimilarityOptions__GroupAssignSimilarityThreshold=0.93
LearningSimilarityOptions__GroupSearchTopK=5
LearningSimilarityOptions__MaxEvidenceLength=20000
RedisEventBusOptions__ConnectionString=localhost:6379
AuthenticationOptions__Enabled=true
AuthenticationOptions__ApiKeys__0=replace-with-a-real-api-key
Cors__AllowedOrigins__0=http://localhost:5170
Hangfire__EnableServer=true
Hangfire__WorkerCount=2
```

Web UI example:

```text
API_BASE_URL=http://localhost:8090
AuthenticationOptions__ApiKeys__0=replace-with-a-real-api-key
```

</details>

## Notes

- `ChatConfig` is runtime-editable through the Web UI and stored in PostgreSQL
- `EmbeddingConfig` is currently runtime-readable but not runtime-editable through the Web UI
- the settings dialog reflects the effective API connection selected in the browser, not just the deployment defaults

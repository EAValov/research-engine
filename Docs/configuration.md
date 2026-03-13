# Configuration Guide

This document describes the runtime configuration currently used by the Research Engine solution.

It is based on the implementation in:

- `ResearchEngine.API`
- `ResearchEngine.WebUI`
- the sample values in [README.md](/c:/LLM/ResearchApi/README.md)
- the deployment example in [Deploy/llm-stack.yaml](/c:/LLM/ResearchApi/Deploy/llm-stack.yaml)

## Configuration Sources

The solution currently uses two configuration layers:

### API

The API is an ASP.NET Core application. Its configuration comes from standard ASP.NET Core providers, including:

- `ResearchEngine.API/appsettings.json`
- environment variables
- any other default ASP.NET Core provider you add later

Environment variables use the standard `__` separator, for example:

```text
ConnectionStrings__ResearchDb
ChatConfig__Endpoint
EmbeddingConfig__Dimension
BearerAuthenticationOptions__BearerTokens__0
```

### Web UI

The Web UI is a Blazor WebAssembly app. At runtime it reads:

- `ResearchEngine.WebUI/wwwroot/appsettings.json`

In the containerized setup, [ResearchEngine.WebUI/entrypoint.sh](/c:/LLM/ResearchApi/ResearchEngine.WebUI/entrypoint.sh) rewrites that file from environment variables before Caddy starts.

## Configuration Precedence

For the API, standard ASP.NET Core precedence applies, so environment variables override values from `appsettings.json`.

For the Web UI container, the generated `/srv/appsettings.json` becomes the effective runtime configuration for the browser app.

## API Configuration

### `ConnectionStrings`

```json
"ConnectionStrings": {
  "ResearchDb": "...",
  "HangfireDb": "..."
}
```

- `ResearchDb` is required.
- `HangfireDb` is optional. If it is not set, the API falls back to `ResearchDb`.

Usage:

- `ResearchDb`
  - main EF Core/PostgreSQL database
  - health check target
- `HangfireDb`
  - Hangfire job storage

Notes:

- PostgreSQL must support the `vector` extension because the app configures pgvector columns and indexes.
- The application runs EF Core migrations automatically on startup outside the `Testing` environment.

Example environment variables:

```text
ConnectionStrings__ResearchDb=Host=localhost;Port=5432;Database=research;Username=app;Password=secret
ConnectionStrings__HangfireDb=Host=localhost;Port=5432;Database=jobs;Username=app;Password=secret
```

### `FirecrawlOptions`

```json
"FirecrawlOptions": {
  "BaseUrl": "http://firecrawl:3002",
  "ApiKey": "...",
  "HttpClientTimeoutSeconds": 600
}
```

- `BaseUrl` is the Firecrawl service base URL.
- `ApiKey` is optional in code, but normally required for real Firecrawl deployments.
- `HttpClientTimeoutSeconds` controls the timeout used for search and scrape requests.

Usage:

- search requests go to `POST {BaseUrl}/v1/search`
- scrape requests go to `POST {BaseUrl}/v1/scrape`

Recommendation:

- use a base URL without a trailing slash, for example `http://firecrawl:3002`

### `ChatConfig`

```json
"ChatConfig": {
  "Endpoint": "http://vllm:8000/v1",
  "ApiKey": "...",
  "ModelId": "nvidia/Qwen3-30B-A3B-NVFP4"
}
```

- `Endpoint` is the OpenAI-compatible chat endpoint base.
- `ApiKey` is optional.
- `ModelId` is required.

Usage:

- chat generation uses the OpenAI SDK against `ChatConfig`
- readiness checks call `{Endpoint}/models`
- token counting uses a tokenizer request derived from this same backend

Important:

- the current tokenizer implementation also expects a `POST /tokenize` endpoint on the same chat backend host
- the sample configuration is written for a vLLM-style setup

### `EmbeddingConfig`

```json
"EmbeddingConfig": {
  "Endpoint": "http://vllm-embeddings:8001/v1",
  "ApiKey": "...",
  "ModelId": "Qwen/Qwen3-Embedding-0.6B",
  "Dimension": 1024
}
```

- `Endpoint` is the OpenAI-compatible embeddings endpoint base.
- `ApiKey` is required by the current implementation.
- `ModelId` is required.
- `Dimension` is required and must match the actual embedding size returned by the model.

Usage:

- embedding generation
- pgvector column size in the database model
- embedding readiness check via `{Endpoint}/models`

Important:

- if `Dimension` does not match the real embedding vector length, persistence/search will break

### `ResearchOrchestratorConfig`

```json
"ResearchOrchestratorConfig": {
  "LimitSearches": 5,
  "MaxUrlParallelism": 1,
  "MaxUrlsPerSerpQuery": 20
}
```

- `LimitSearches` limits the number of search results requested per SERP query.
- `MaxUrlParallelism` limits how many URLs are processed concurrently for learning extraction.
- `MaxUrlsPerSerpQuery` caps how many URLs from one SERP query are crawled.

These values directly affect crawl speed, model load, and total job cost.

### `LearningSimilarityOptions`

The code binds this section with startup validation.

Current implementation:

```json
"LearningSimilarityOptions": {
  "MinImportance": 0.4,
  "MinLocalFractionForNoGlobal": 0.75,
  "DiversityMaxPerUrl": 3,
  "DiversityMaxTextSimilarity": 0.85
}
```

- `MinImportance`
  - minimum learning importance score considered during synthesis retrieval
- `MinLocalFractionForNoGlobal`
  - present in code and validated, but not currently used by retrieval logic
- `DiversityMaxPerUrl`
  - limits how many learnings from the same source URL survive the diversity filter
- `DiversityMaxTextSimilarity`
  - suppresses near-duplicate learning texts during synthesis retrieval

Important mismatch in the current sample config:

- `ResearchEngine.API/appsettings.json` currently contains:

```json
"LearningSimilarityOptions": {
  "LocalMinImportance": 0.4,
  "GlobalMinImportance": 0.65,
  "MinLocalFractionForNoGlobal": 0.75,
  "DiversityMaxPerUrl": 3,
  "DiversityMaxTextSimilarity": 0.85
}
```

- but the code only consumes `MinImportance`
- `LocalMinImportance` and `GlobalMinImportance` are not read by the current implementation

For actual runtime behavior, document and set `MinImportance`.

### `BearerAuthenticationOptions`

```json
"BearerAuthenticationOptions": {
  "Enabled": true,
  "BearerTokens": [
    "token-1"
  ]
}
```

- `Enabled`
  - when `false`, the custom bearer handler returns `NoResult` and protected endpoints effectively stop requiring a token
- `BearerTokens`
  - allowed bearer tokens for API access

Usage:

- most `/api/*` and `/api/protocol/*` endpoints require authorization
- the SSE events stream endpoint is anonymous, but it uses a ticket flow managed by the API

Container note:

- the Web UI container copies `BearerAuthenticationOptions__BearerTokens__0` into its browser config as the default bearer token

Recommendation:

- do not store real bearer tokens in source-controlled JSON files
- prefer environment variables or secrets

### `RedisEventBusOptions`

```json
"RedisEventBusOptions": {
  "ConnectionString": "redis:6379"
}
```

- `ConnectionString` is required and validated on startup

Usage:

- Redis pub/sub event bus
- Redis health check
- distributed cache used by IP rate limiting when rate limiting is enabled

### `Hangfire`

This section is not present in the sample `appsettings.json`, but it is used by code.

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
  - when `false`, the API runs without starting Hangfire workers
- `WorkerCount`
  - default `2`
  - controls Hangfire server worker count
- `QueuePollMs`
  - optional
  - sets the PostgreSQL Hangfire queue polling interval in milliseconds

Queues used by the app:

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

- `AllowedOrigins` defines the origins allowed by the `WebUIDev` CORS policy

If this section is missing or empty, the API falls back to these defaults:

- `http://localhost:5170`
- `http://127.0.0.1:5170`
- `http://localhost:5173`
- `http://127.0.0.1:5173`
- `https://localhost:5001`
- `http://localhost:5000`

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

- counters and policies are stored in Redis
- when enabled, the app trusts forwarded headers and reads `X-Forwarded-For`

### `Serilog`

Logging is configured from the `Serilog` section.

The sample config writes logs to:

- console
- `logs/app.log` with hourly rolling files

You can change sink configuration, log levels, and retention entirely through the `Serilog` section.

## Web UI Configuration

### `ApiBaseUrl`

```json
"ApiBaseUrl": "http://localhost:8090"
```

- base URL used by the browser app for API requests
- must be an absolute `http` or `https` URL
- trailing slashes are normalized automatically

Fallback:

- if no valid value is present, the Web UI falls back to `http://localhost:8090`

### `ApiAuth`

```json
"ApiAuth": {
  "BearerToken": ""
}
```

- initial bearer token used by the browser app
- this is only the default value; the UI lets the user change API URL, enable/disable auth, and update the token at runtime

Container behavior:

- `API_BASE_URL` becomes `ApiBaseUrl`
- `BearerAuthenticationOptions__BearerTokens__0` becomes `ApiAuth:BearerToken`

## Recommended Secret Handling

Treat these values as secrets and inject them outside source control:

- `ConnectionStrings__ResearchDb`
- `ConnectionStrings__HangfireDb`
- `FirecrawlOptions__ApiKey`
- `ChatConfig__ApiKey`
- `EmbeddingConfig__ApiKey`
- `BearerAuthenticationOptions__BearerTokens__*`

## Minimal API Example

This is a minimal environment-variable configuration for a local run:

```text
ConnectionStrings__ResearchDb=Host=localhost;Port=5432;Database=research;Username=app;Password=secret
ConnectionStrings__HangfireDb=Host=localhost;Port=5432;Database=jobs;Username=app;Password=secret
FirecrawlOptions__BaseUrl=http://localhost:3002
FirecrawlOptions__ApiKey=your-firecrawl-key
FirecrawlOptions__HttpClientTimeoutSeconds=600
ChatConfig__Endpoint=http://localhost:8000/v1
ChatConfig__ApiKey=your-chat-key
ChatConfig__ModelId=your-chat-model
EmbeddingConfig__Endpoint=http://localhost:8001/v1
EmbeddingConfig__ApiKey=your-embedding-key
EmbeddingConfig__ModelId=your-embedding-model
EmbeddingConfig__Dimension=1024
ResearchOrchestratorConfig__LimitSearches=5
ResearchOrchestratorConfig__MaxUrlParallelism=1
ResearchOrchestratorConfig__MaxUrlsPerSerpQuery=20
LearningSimilarityOptions__MinImportance=0.4
LearningSimilarityOptions__MinLocalFractionForNoGlobal=0.75
LearningSimilarityOptions__DiversityMaxPerUrl=3
LearningSimilarityOptions__DiversityMaxTextSimilarity=0.85
RedisEventBusOptions__ConnectionString=localhost:6379
BearerAuthenticationOptions__Enabled=true
BearerAuthenticationOptions__BearerTokens__0=replace-with-a-real-token
Cors__AllowedOrigins__0=http://localhost:5170
Hangfire__EnableServer=true
Hangfire__WorkerCount=2
```

For the Web UI:

```text
API_BASE_URL=http://localhost:8090
BearerAuthenticationOptions__BearerTokens__0=replace-with-a-real-token
```

## Known Gaps

These are worth keeping in mind while reviewing the current configuration model:

- `LearningSimilarityOptions` in the sample JSON is partly out of sync with the code
- `ConnectionStrings__Redis` appears in the deployment manifest, but the current API code uses `RedisEventBusOptions:ConnectionString` instead
- `ChatConfig` and `EmbeddingConfig` are not validated on startup, so invalid values may fail later when the related service is first used

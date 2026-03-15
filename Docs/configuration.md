# Configuration Guide

This document describes the current runtime configuration for the Research Engine solution.

It is based on:

- `ResearchEngine.API`
- `ResearchEngine.WebUI`
- `ResearchEngine.API/appsettings.json`
- the single-host deployment manifests in `Deploy/single-host`

## Configuration Sources

### API

The API uses standard ASP.NET Core configuration providers.

In practice that means values can come from:

- `ResearchEngine.API/appsettings.json`
- environment variables

Environment variables use the standard `__` separator, for example:

```text
ConnectionStrings__ResearchDb
ChatConfig__Endpoint
EmbeddingConfig__Dimensions
```

### Web UI

The Web UI is a Blazor WebAssembly app.

It has two layers of configuration:

- startup defaults from `ResearchEngine.WebUI/wwwroot/appsettings.json`
- browser-local overrides stored by the settings dialog

In the containerized deployment, `ResearchEngine.WebUI/entrypoint.sh` rewrites the runtime `appsettings.json` before Caddy starts.

## Precedence

### API config values priority

Standard ASP.NET Core precedence applies.

That means environment variables override `appsettings.json`.

This matters for the runtime settings UI:

- the UI updates `ResearchOrchestratorConfig` and `LearningSimilarityOptions` by writing to `appsettings.json`
- those changes take effect only if the same keys are not being overridden by environment variables

In the current `Deploy/single-host` manifests:

- `ResearchOrchestratorConfig` is not overridden by env vars
- `LearningSimilarityOptions` is not overridden by env vars
- `ChatConfig` is overridden by env vars
- `EmbeddingConfig` is overridden by env vars
- several other API settings are overridden by env vars as well

### Web UI precedence

For the browser app:

1. deployment-generated `appsettings.json` provides defaults
2. browser-local saved settings override those defaults for that user

So the API URL and API key shown in the UI are user-overridable even if the container started with default values from environment variables.

## API Configuration

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
- outside the `Testing` environment, migrations are applied automatically on startup

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
- the current sample setup is tuned for a single RTX 5090 and using the `nvidia/Qwen3-30B-A3B-NVFP4` model.

Single-host deployment example:

```yaml
env:
  - name: ChatConfig__Endpoint
    value: "http://research-model:8000/v1"
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
- The examle config uses `Qwen/Qwen3-Embedding-0.6B` embedding model. 

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
  "MaxUrlsPerSerpQuery": 20
}
```

This section is validated on startup.

Current validation:

- `LimitSearches`: `1..1000`
- `MaxUrlParallelism`: `1..1000`
- `MaxUrlsPerSerpQuery`: `1..1000`

Meaning:

- `LimitSearches`
  - maximum number of search results requested for each SERP query
- `MaxUrlParallelism`
  - maximum number of source URLs processed in parallel for one SERP query
- `MaxUrlsPerSerpQuery`
  - maximum number of unique URLs processed from a SERP query

These settings are live-readable through `IOptionsMonitor` and are exposed in the Web UI settings dialog.

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

These settings are live-readable through `IOptionsMonitor` and are exposed in the Web UI settings dialog.

In the current `Deploy/single-host` deployment, this section is not overridden by environment variables, so UI changes remain effective.

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

## API Runtime Settings Endpoint

The API exposes a runtime settings endpoint:

- `GET /api/settings/runtime`
- `PUT /api/settings/runtime`

It currently supports:

- `ResearchOrchestratorConfig`
- `LearningSimilarityOptions`
- read-only model information from `ChatConfig` and `EmbeddingConfig`

Behavior:

- `PUT` writes the updated values to `ResearchEngine.API/appsettings.json`
- the API reloads configuration immediately after writing
- live consumers that use `IOptionsMonitor` pick up the new values

Current live-update behavior:

- `ResearchOrchestratorConfig`: live
- `LearningSimilarityOptions`: live
- `ChatConfig`: displayed, not updated by the endpoint
- `EmbeddingConfig`: displayed, not updated by the endpoint

Important:

- environment-variable overrides still win over values written to `appsettings.json`

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

The same dialog also loads and updates the API runtime settings described above.

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
- chat endpoint, model id, and API key
- embedding endpoint, model id, dimension, and API key
- Redis connection string
- API key auth enabled flag and API key
- Web UI default API URL

The same manifests currently do not override:

- `ResearchOrchestratorConfig`
- `LearningSimilarityOptions`

That is why the Web UI runtime settings editor can change those two groups successfully in the deployed single-host setup.

## Secrets

Treat these values as secrets and inject them outside source control:

- `ConnectionStrings__ResearchDb`
- `ConnectionStrings__HangfireDb`
- `FirecrawlOptions__ApiKey`
- `ChatConfig__ApiKey`
- `EmbeddingConfig__ApiKey`
- `AuthenticationOptions__ApiKeys__*`

The single-host example stores them in `Deploy/single-host/00-common.yaml` as a Kubernetes-style `Secret` manifest for local deployment.

## Minimal Environment Variable Example

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

## Notes

- `ChatConfig` and `EmbeddingConfig` are currently runtime-readable but not runtime-editable through the Web UI
- `ResearchOrchestratorConfig` and `LearningSimilarityOptions` are the only settings groups currently updated by the runtime settings editor
- the settings dialog reflects the effective API connection selected in the browser, not just the deployment defaults

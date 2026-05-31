# Deployment Guide

Research Engine now uses Aspire as the local orchestration source of truth.

Aspire replaces the old hand-written Podman kube and compose orchestration with a code-defined AppHost in `ResearchEngine.AppHost`. The API and WebUI still publish normal OCI images to GitHub Container Registry, so users can also run release artifacts without source code or a local .NET SDK.

## Deployment Paths

There are two supported paths:

- **Source local deployment**: run `aspire run --project ResearchEngine.AppHost` from the repository. This starts the API and WebUI as .NET source projects and starts backing services as containers.
- **Container-only release deployment**: download the generated Aspire Docker Compose bundle from a GitHub Release. This uses the published `research-api` and `research-webui` images and does not require source code or the .NET runtime on the target machine.

Aspire still needs an OCI-compatible container runtime for local containers. Podman and Docker are both valid choices. The default AppHost options are tuned for Podman on Windows/WSL; Docker users may want `--container-runtime docker --host-gateway host.docker.internal`.

## Recommended User Flow

1. Install the Aspire CLI:

   ```powershell
   dotnet tool install -g aspire.cli
   ```

2. Install and start Podman or Docker.

3. If you want the full local model stack, configure GPU support for your container runtime.

4. Start the full stack:

   ```powershell
   aspire run --project ResearchEngine.AppHost/ResearchEngine.AppHost.csproj
   ```

5. Open:

   ```text
   http://localhost:8090/
   ```

6. Optional: add friendly HTTPS hostnames to your hosts file:

   ```text
   127.0.0.1 research-webui.llm.local
   127.0.0.1 research-api.llm.local
   ```

7. Optional: start and trust the local Caddy CA certificate:

   ```powershell
   aspire run --project ResearchEngine.AppHost/ResearchEngine.AppHost.csproj -- --install-caddy-ca
   ```

8. Then open:

   ```text
   https://research-webui.llm.local:8443/
   https://research-api.llm.local:8443/
   ```

## Full Local Profile

The full profile is the default:

```powershell
aspire run --project ResearchEngine.AppHost/ResearchEngine.AppHost.csproj
```

It starts:

- `research-api`
- `research-webui`
- `research-postgres`
- `research-redis`
- `research-ollama`
- `research-ollama-init`
- Firecrawl stack: `research-crawl`, `firecrawl-playwright`, `searxng`, `crawl-postgres`, `crawl-redis`, `crawl-rabbitmq`
- `research-llm` with vLLM
- `research-edge` with Caddy

The API waits for PostgreSQL, Redis, Ollama, the embedding model pull, Firecrawl, and vLLM. The WebUI waits for the API, and Caddy waits for both app projects.

The default vLLM command and container runtime arguments live in `ResearchEngine.AppHost/apphost.env`.

`VLLM_ARGS` is intentionally one string so model-specific command lines can be replaced as a unit:

```text
VLLM_ARGS={CHAT_MODEL_ID} --host {APP_BIND_HOST} --port {VLLM_PORT} --gpu-memory-utilization 0.94 --max-model-len auto --max-num-seqs 1 --enable-auto-tool-choice --tool-call-parser openai
```

`{CHAT_MODEL_ID}`, `{APP_BIND_HOST}`, and `{VLLM_PORT}` are expanded by the AppHost after CLI and environment overrides are applied.

The default container runtime args assume Podman-style NVIDIA GPU access:

```text
VLLM_CONTAINER_RUNTIME_ARGS=--device=nvidia.com/gpu=all
```

Override either value in `apphost.env`, with process environment variables, or with `--vllm-args` / `--vllm-container-runtime-args`.

## Light Local Profile

Use the light profile when chat and crawl services are external:

```powershell
aspire run --project ResearchEngine.AppHost/ResearchEngine.AppHost.csproj -- --profile light --chat-endpoint https://openrouter.ai/api/v1 --chat-api-key <key> --chat-model-id <model> --firecrawl-base-url https://api.firecrawl.dev --firecrawl-api-key <key>
```

The light profile starts only:

- API and WebUI source projects
- app PostgreSQL
- app Redis
- Ollama embeddings and the embedding model pull helper
- Caddy edge

It does not start vLLM or Firecrawl locally. Aspire can configure and show the app that depends on those external services, but it cannot lifecycle-manage services outside the AppHost.

## Common AppHost Options

Default AppHost values live in `ResearchEngine.AppHost/apphost.env`. AppHost options can be passed after `--`, and have this precedence:

```text
CLI flag > process environment variable > ResearchEngine.AppHost/apphost.env
```

You can point at another defaults file with `--defaults-file <path>` or `RESEARCH_ASPIRE_DEFAULTS_FILE`.

| Option | Env key | Purpose |
| --- | --- | --- |
| `--profile full|light` | `RESEARCH_ASPIRE_PROFILE` | Selects local topology. |
| `--app-mode source|images` | `RESEARCH_ASPIRE_APP_MODE` | Runs API/WebUI from source or from published images. |
| `--container-runtime podman|docker` | `RESEARCH_CONTAINER_RUNTIME` | Used by the certificate helper. |
| `--host-gateway <host>` | `RESEARCH_ASPIRE_HOST_GATEWAY` | Hostname Caddy uses to reach source projects from a container. Use `host.docker.internal` for Docker Desktop. |
| `--api-image <image>` | `RESEARCH_API_IMAGE` | API image reference for `--app-mode images`. |
| `--webui-image <image>` | `RESEARCH_WEBUI_IMAGE` | WebUI image reference for `--app-mode images`. |
| `--api-key <key>` | `RESEARCH_API_KEY` | Shared API key injected into API and WebUI. |
| `--chat-endpoint <url>` | `CHAT_ENDPOINT` | External chat endpoint for light mode. |
| `--firecrawl-base-url <url>` | `FIRECRAWL_BASE_URL` | External crawl endpoint for light mode. |
| `--vllm-args <args>` | `VLLM_ARGS` | Complete vLLM server argument string. |
| `--vllm-container-runtime-args <args>` | `VLLM_CONTAINER_RUNTIME_ARGS` | Container runtime args for GPU/device access. |

Less common image tags, resource names, ports, bind paths, and volume names are also in `apphost.env`.

## Container-Only Release Bundle

Published releases include:

- `ghcr.io/eavalov/research-api:<version>`
- `ghcr.io/eavalov/research-webui:<version>`
- a generated Aspire Docker Compose bundle attached to the GitHub Release

The compose bundle is generated from the same AppHost model with:

```powershell
aspire publish --project ResearchEngine.AppHost/ResearchEngine.AppHost.csproj -o artifacts/research-engine-compose -- --profile full --app-mode images --api-image ghcr.io/eavalov/research-api:<version> --webui-image ghcr.io/eavalov/research-webui:<version>
```

Use `--profile light` instead when chat and crawl are external.

That bundle is intended for users who want to run containers without cloning the repository or installing the .NET runtime. The generated compose file has concrete image names, credentials, ports, and relative bind mounts, so it can be run directly from the bundle directory.

On Windows, `podman compose` may prefer Docker Compose if it is installed. If that happens, Podman reports a Docker daemon connection error before the stack starts. Install `podman-compose` and point Podman at it:

```powershell
python -m pip install --user podman-compose
$provider = Join-Path (python -m site --user-base) "Scripts\podman-compose.exe"
[Environment]::SetEnvironmentVariable("PODMAN_COMPOSE_PROVIDER", $provider, "User")
```

Then start the bundle:

```powershell
podman compose -f docker-compose.yaml up -d
```

## Hardware Sizing Guide

For full local deployment, the model server is the main constraint.

The current default is:

- `openai/gpt-oss-20b`
- vLLM `v0.20.1`
- GPU memory utilization `0.94`
- max model length `auto`
- max sequences `1`

The chat backend must support:

- OpenAI-compatible `/v1/chat/completions`
- `/v1/models`
- structured output with JSON schema
- tool calling with required/specific function choice
- either `/tokenize` or a realistic `ChatConfig__MaxContextLength`

The app uses Ollama for embeddings by default with `qwen3-embedding:0.6b` and vector dimension `1024`.

## Legacy Files

The previous `Deploy/single-host` Podman kube manifests and `Deploy/compose/compose.yaml` are kept during migration validation only. Aspire is the intended orchestration source going forward.

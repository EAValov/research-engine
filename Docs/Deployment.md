# Deployment Guide

This guide covers two supported deployment paths:

- `Deploy/compose/compose.yaml` for a lightweight app-only deployment using released images and external chat and web crawl services
- `Deploy/single-host` for the full local Podman-based stack with edge, crawl, and model pods

If you want to move to Kubernetes later, use this document and the files in `Deploy/single-host` as the baseline.

Podman is the recommended platform because it is secure and open source. The full single-host setup was tested on Windows 11 with WSL2 and Podman, and it works well there.

If you have an NVIDIA GPU with around `16 GB` of VRAM or more and want the full local stack, you can jump straight to the `Recommended User Flow` section. Otherwise, you will likely need to tune the LLM server for your machine. Research quality and speed mainly depend on the model backend. See `LLM Server configuration` for details.

## Compose Setup

`Deploy/compose/compose.yaml` is the lightest app deployment option. It starts the app itself locally and points it at a chat backend and a crawl backend over HTTP. Those backends can be cloud-hosted or self-hosted separately.

What it starts locally:

- `research-webui`
- `research-api`
- `research-postgres`
- `research-redis`
- `research-ollama`
- `research-ollama-init` as a one-shot helper that pulls the configured embedding model and then exits

What it expects externally:

- an OpenAI-compatible chat backend for planning and synthesis
- a Firecrawl endpoint for search and scraping

The compose deployment pulls the released `research-api` and `research-webui` images from GitHub Container Registry instead of building them locally. The Web UI container mounts `Deploy/compose/Caddyfile`, proxies API routes to `research-api`, and exposes a single browser URL at `http://localhost:8090/`.
By default it uses the `latest` container tag, and you can pin a specific release by changing `RESEARCH_ENGINE_TAG` in `Deploy/compose/.env`.
Seeing `research-ollama-init` in an exited state after a successful `compose up` is expected. It only preloads the embedding model into the shared Ollama volume and does not stay running.

### Compose Setup With Cloud Services

Use this if you want to use Firecrawl Cloud and a cloud LLM provider.

> Note:
> Instead of cloud services, you can just deploy vLLM and Firecrawl separately and point the same compose file at those endpoints.
> - vLLM Docker deployment: https://docs.vllm.ai/en/stable/deployment/docker/
> - Firecrawl self-host guide: https://github.com/firecrawl/firecrawl/blob/main/SELF_HOST.md

1. Create a Firecrawl account at `firecrawl.dev` and get your API key.
2. Create an API key with an OpenAI-compatible chat provider.
3. Change into the compose deployment folder:

   ```powershell
   cd .\Deploy\compose
   ```

4. Copy `.env.example` to `.env`.
5. Set these values:

   - `RESEARCH_DB_PASSWORD`
   - `RESEARCH_API_KEY`
   - `CHAT_ENDPOINT=https://openrouter.ai/api/v1` if you use OpenRouter
    - `CHAT_API_KEY=<your-llm-api-key>`
    - `CHAT_MODEL_ID=<your-model-id>`
    - `CHAT_MAX_CONTEXT_LENGTH=<the real context limit of your selected model>`
    - `FIRECRAWL_BASE_URL=https://api.firecrawl.dev` or your self-hosted firecrawl instance
    - `FIRECRAWL_API_KEY=<your-firecrawl-api-key>`

6. Recommended: if your LLM backend does not expose `/tokenize`, set `CHAT_MAX_CONTEXT_LENGTH` to the real model context limit you selected. Most of the OpenAI-compatible cloud providers don't have this.

7. Start the stack:

   ```powershell
   docker compose up -d
   ```

   or:

   ```powershell
   podman compose up -d
   ```

8. Open:

   ```text
   http://localhost:8090/
   ```
   
## Full Single-Host Podman Layout

The recommended single-host layout is four pods with clear responsibility boundaries:

- Edge Pod
- App Pod
- Crawl Pod
- Model Pod

This alignment keeps the stack easy to replace and scale without turning it into one large pod. If you prefer, you can also run a single PostgreSQL container and a single Redis container for the whole stack.

Secrets are scoped per component manifest so you can skip a component without deploying unrelated secrets:

- `Deploy/single-host/20-app.yaml` contains app-related secrets.
- `Deploy/single-host/30-crawl.yaml` contains crawl-related secrets.

## Edge Pod

Services:

- `caddy`

Purpose:

- provide HTTPS
- provide the friendly local address `https://research-webui.llm.local:8443/`
- provide the friendly local API address `https://research-api.llm.local:8443/`
- provide a plain HTTP app entrypoint at `http://localhost:8090/` without requiring hosts-file or certificate setup
- route UI traffic to `research-webui`
- route API traffic to `research-api`
- optionally load-balance multiple `research-api` instances later
- persist the local Caddy CA state so the trusted certificate survives `.\Deploy\single-host.ps1 up` redeploys

## App Pod

Services:

- `research-webui`
- `research-api`
- `research-postgres`
- `research-redis`
- `ollama`

For the full single-host Podman flow, `research-webui` and `research-api` are built locally by `.\Deploy\single-host.ps1 up` and `.\Deploy\single-host.ps1 restart` by default. Published release images are also available in GitHub Container Registry and are used by `Deploy/compose/compose.yaml`.

`ollama` is not optional and is used to generate embeddings. You can replace it with any OpenAI-compatible embeddings service. Ollama is used here because it is easy to deploy. In the example setup, it uses the CPU to compute embeddings and keep some load off the GPU.

`research-api` supports horizontal scaling and rate limiting. See [Configuration](./Configuration.md).

## Crawl Pod

Services:

- `firecrawl`
- `firecrawl-playwright`
- `searxng`
- `crawl-postgres`
- `crawl-redis`

For a self-hosted deployment, follow the Firecrawl docs:

- https://docs.firecrawl.dev/contributing/self-host

You can also use the Firecrawl API and a Firecrawl service subscription instead of deploying this pod.

This project is not sponsored by or affiliated with Firecrawl. It is used here because switching between the self-hosted and cloud versions is easy. Support for Crawl4AI is coming soon:

- https://github.com/unclecode/crawl4ai

## Model Pod

Services:

- `vllm`

For a local deployment, follow the vLLM docs:

- https://docs.vllm.ai/en/latest/serving/openai_compatible_server.html

Any OpenAI-compatible web service can be used instead of `vllm`. The current single-host example is tuned for a single `16 GB` NVIDIA GPU and uses `openai/gpt-oss-20b` as a conservative local baseline.
Use [`Deploy/single-host/40-llm.yaml`](../Deploy/single-host/40-llm.yaml) as the main local `vllm` reference. That manifest is where you normally adjust the model id, model-specific quantization settings, GPU memory target, and maximum context length for your hardware.

### LLM Server requirements

1. **[Required]** The backend and model must support tool calling with `tool_choice: required`, because the research pipeline depends on it.
2. **[Required]** The backend and model must support structured output with a JSON schema.
3. **[Optional]** The app can use a `/tokenize` endpoint in the internal RAG pipeline to split content more accurately. Without it, the app falls back to `MaxContextLength` and a heuristic estimate with a 20% safety buffer.

vLLM is the preferred backend because it supports all of these features. Ollama also works, but it does not expose `/tokenize`. Below is the compatibility chart:

#### Chat Backend Compatibility

| Backend | `/v1/models` | Structured output | Tool calling | `/tokenize` | Verdict |
| --- | --- | --- | --- | --- | --- |
| `vLLM` | Yes | Yes | Yes, including named-function and `required` tool choice | Yes | Preferred local option. It supports all required features for the current app and is the recommended self-hosted backend. |
| `Ollama` | Yes | Yes | Yes | No | Works with the current app if `ChatConfig__MaxContextLength` is set. This is usable, but still less ideal than `vLLM` because token counting becomes heuristic. |
| `LM Studio` | Yes | No | Yes, but `tool_choice: required` is not supported | No | Current version 0.4.6 will not work. |

### LLM Server configuration

The most important part of the configuration is choosing the right model. The goal is to find the best balance between quality and speed.
Speed matters too: the slower the model, the slower the research.
As a rule of thumb, the model should process at least 50 tokens per second if you want to generate a mid-size report in around 5-10 minutes.

Hint: The app supports both thinking and non-thinking models.

The chat model should have a context window of at least `10000`. Smaller context windows degrade quality a lot because the planner, section writer, and learning extraction prompts need room to work.

#### Hardware Sizing Guide

[`Deploy/single-host/40-llm.yaml`](../Deploy/single-host/40-llm.yaml) is the reference example for the local `vllm` pod. The current sample manifest assumes:

- `openai/gpt-oss-20b`
- `--gpu-memory-utilization 0.94`
- `--max-model-len 10240`
- `--max-num-seqs 1`
- a single `16 GB` NVIDIA GPU

That is meant as a conservative `16 GB`-friendly baseline rather than a maximum-quality configuration.

- The current `Deploy/single-host/40-llm.yaml` container image is the NVIDIA/CUDA variant of `vLLM`. If you want to run on AMD, switch to the official ROCm image such as `vllm/vllm-openai-rocm` and adjust the container runtime/device configuration accordingly. For Intel there is a guide [guide](https://docs.vllm.ai/en/latest/getting_started/installation/gpu/#intel-xpu) - You would have to build the vLLM image locally.
- If you have `16 GB` of VRAM, start with the current sample manifest and only raise the context length after you confirm it is stable on your card. The RAM offloading reduces the speed a lot.
- The context length is important. The minimum is `10000` because the internal RAG pipeline degrades noticeably below that. More context improves the learning extraction speed and quality. The current sample uses `10240` 
- Smaller models and lower context lengths usually trade some peak quality for stability and speed, but they can still work quite well for this app. In practice, that is often a better outcome than running an oversized model too slowly or out of memory.
- `openai/gpt-oss-20b` is the current default example because it fits `16 GB` cards well and supports the structured-output and tool-calling features this app needs.
- The app has been tested mainly with the Qwen3 family. In the author's testing, Qwen3 models have been the most capable for this workload so far, so `Qwen3-14B-AWQ` or `Qwen3-30B-A3B-NVFP4` are strong alternatives if you want to tune for higher quality or have more GPU headroom. Important is that the model needs to support tool calling.

#### What To Change When You Swap Models

For a local `vllm` deployment, these are the main knobs:

- In [`Deploy/single-host/40-llm.yaml`](../Deploy/single-host/40-llm.yaml), change the model id, any model-specific quantization settings, `--max-model-len`, and optionally `--gpu-memory-utilization`.
- In [`Deploy/single-host/20-app.yaml`](../Deploy/single-host/20-app.yaml), keep `ChatConfig__ModelId` aligned with the model served by the backend.
- If your backend does not expose `/tokenize`, set `ChatConfig__MaxContextLength` in [`Deploy/single-host/20-app.yaml`](../Deploy/single-host/20-app.yaml) to match the real backend limit you chose.
- Make these edits before the first startup when possible. After first startup, `ChatConfig` is stored in PostgreSQL, so later `ChatConfig__*` environment-variable changes will not override the existing runtime settings row until you update it through the app or reset that row.

## Recommended User Flow

This is the recommended setup flow for a clean Windows installation with current NVIDIA drivers:

1. Install Podman.
2. If you want to run the local `vllm` pod, configure GPU passthrough for Podman:
   https://podman-desktop.io/docs/podman/gpu
3. Decide which components you want to run locally and update `Deploy/single-host/20-app.yaml` if you plan to use cloud services:

   - Cloud crawl: set `FirecrawlOptions__BaseUrl` and `FirecrawlOptions__ApiKey` to your provider values.
   - Cloud model: set `ChatConfig__Endpoint`, `ChatConfig__ApiKey`, and `ChatConfig__ModelId` to your provider values.
   - Local-by-default values already point to `research-crawl` and `research-llm`.

4. Deploy the full stack:

   ```powershell
   .\Deploy\single-host.ps1 up
   ```

   This script builds the local `research-api` and `research-webui` images first, then deploys all single-host manifests (`app`, `crawl`, `llm`, `edge`).
   The first startup can take several minutes while `vLLM` downloads the model and compiles kernels.
   If you need to exclude some services, deploy manifests manually with Podman:

   ```powershell
   podman kube play --replace .\Deploy\single-host\20-app.yaml
   podman kube play --replace .\Deploy\single-host\10-edge.yaml
   ```

5. Open the app:

   ```text
   http://localhost:8090/
   ```

6. Optional: add these entries to `C:\Windows\System32\drivers\etc\hosts` if you want the friendly HTTPS URLs:

   ```text
   127.0.0.1 research-webui.llm.local
   127.0.0.1 research-api.llm.local
   ```

   You can choose any local domain names here.

7. Optional but recommended: install the local Caddy certificate:

   ```powershell
   .\Deploy\single-host.ps1 up -InstallCaddyCertificate
   ```

   This is safe in the normal local-deployment sense: it trusts the local CA generated by your own Caddy container so Windows and your browser accept the local HTTPS URL without warnings.

   If you already started the stack and only want to trust the certificate later, you can still run:

   ```powershell
   .\Deploy\trust-caddy-local-ca.ps1
   ```

8. If you want the friendly HTTPS URL, open:

   ```text
   https://research-webui.llm.local:8443/
   ```
   The Web UI will use the current origin by default, so this direct API endpoint also works:

   ```text
   https://research-api.llm.local:8443/
   ```
   For local Web UI debugging from VS Code, use:

   ```text
   http://localhost:8090/
   ```
9. You're ready to go. Good luck with your research!

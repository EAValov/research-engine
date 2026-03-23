# Deployment Guide

This guide focuses on the simplest supported setup: a single-host deployment.

If you want to move to Kubernetes later, use this document and the files in `Deploy/single-host` as the baseline.

Podman is the recommended platform because it is secure and open source. The example setup was tested on Windows 11 with WSL2 and Podman, and it works well there.

My PC specs for reference:
- CPU: AMD Ryzen 7940HX (16 cores)
- RAM: 32 GB DDR5 5200
- GPU: Nvidia RTX 5090

If you have similar hardware, you can jump straight to the `Recommended User Flow` section. Otherwise, you will likely need to tune the LLM server for your machine. Research quality and speed mainly depend on the model backend. See `LLM Server configuration` for details.

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
- route UI traffic to `research-webui`
- route API traffic to `research-api`
- optionally load-balance multiple `research-api` instances later
- persist the local Caddy CA state so the trusted certificate survives `.\Deploy\single-host.ps1 up` redeploys

`http://localhost:8080/` still works and can be used as a fallback, but the friendly HTTPS address is the recommended path.

## App Pod

Services:

- `research-webui`
- `research-api`
- `research-postgres`
- `research-redis`
- `ollama`

`research-webui` and `research-api` are built locally by `.\Deploy\single-host.ps1 up` and `.\Deploy\single-host.ps1 restart` by default. Container images will be published to GitHub Container Registry later and the example configuration will be updated with those image URLs.

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

Any OpenAI-compatible web service can be used instead of `vllm`. The current sample setup is tuned for a single RTX 5090. 

### LLM Server requirements

1. [Required] The backend and model must support tool calling with `tool_choice: required`, because the research pipeline depends on it.
2. [Required] The backend and model must support structured output with a JSON schema.
3. [Optional] The app can use a `/tokenize` endpoint in the internal RAG pipeline to split content more accurately. Without it, the app falls back to `MaxContextLength` and a heuristic estimate with a 20% safety buffer.

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
As a rule of thumb, the model should process at least 50 tokens per second if you want to generate a mid-size report in around 8-10 minutes.

Hint: The app supports both thinking and non-thinking models.

The chat model should have a context window of at least `10000`. Smaller context windows degrade quality a lot because the planner, section writer, and learning extraction prompts need room to work.

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
   If you need to exclude some services, deploy manifests manually with Podman:

   ```powershell
   podman kube play --replace .\Deploy\single-host\20-app.yaml
   podman kube play --replace .\Deploy\single-host\10-edge.yaml
   ```

5. Add this entry to `C:\Windows\System32\drivers\etc\hosts` if you want the friendly HTTPS URL:

   ```text
   127.0.0.1 research-webui.llm.local
   ```

   You can choose any domain name here.

6. Optional but recommended: install the local Caddy certificate:

   ```powershell
   .\Deploy\single-host.ps1 up -InstallCaddyCertificate
   ```

   This is safe in the normal local-development sense: it trusts the local CA generated by your own Caddy container so Windows and your browser accept the local HTTPS URL without warnings.

   If you already started the stack and only want to trust the certificate later, you can still run:

   ```powershell
   .\Deploy\trust-caddy-local-ca.ps1
   ```

7. Open the app:

   ```text
   https://research-webui.llm.local:8443/
   ```
8. You're ready to go. Good luck with your research!

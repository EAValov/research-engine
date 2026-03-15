# Deployment Guide

Welcome to the deployment guide for Research Engine. This guide is focused on a single-host deployment because it is the simplest option.

If you're planning to deploy it to Kubernetes, that is very much possible. Use this document and the YAML files in `Deploy/single-host` as a base.

Podman is the recommended platform because it is secure and open source. The example setup was tested on Windows 11 with WSL and Podman, and it works well there.

My PC specs for reference:
- CPU: AMD Ryzen 7940HX (16 cores)
- RAM: 32 GB DDR5 5200
- GPU: Nvidia RTX 5090 Founders Edition

If you have the similar PC, you can jump into `Recommended User Flow` section and just follow the steps there - otherwise, you'd need to configure LLM server for your hardware. Research quality and speed mainly depend on the LLM. See `Picking the right model` section for details.

The recommended single-host layout is four pods with clear responsibility boundaries:

- Edge Pod
- App Pod
- Crawl Pod
- Model Pod

This alignment keeps the stack easy to replace and scale without turning it into one large pod. If you prefer, you can also run a single PostgreSQL container and a single Redis container for the whole stack.

## Edge Pod

Services:

- `caddy`

Purpose:

- provide HTTPS
- provide the friendly local address `https://research-webui.llm.local:8443/`
- route UI traffic to `research-webui`
- route API traffic to `research-api`
- optionally load-balance multiple `research-api` instances later

`http://localhost:8080/` still works and can be used as a fallback, but the friendly HTTPS address is the recommended path.

## App Pod

Services:

- `research-webui`
- `research-api`
- `research-postgres`
- `research-redis`
- `ollama`

Build `research-webui` and `research-api` from source for now. Container images will be published to GitHub Container Registry later and the example configuration will be updated with those image URLs.

`ollama` is not optional and is used to generate embeddings. You can replace it with any OpenAI-compatible embeddings service. Ollama is used here because it is easy to deploy. In the example setup, it uses the CPU to compute embeddings and keep some load off the GPU.

`research-api` supports horizontal scaling and rate limiting. See `Docs/configuration.md`.

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

Any OpenAI-compatible web service can be used instead of `vllm`. The current sample setup is tuned for a single RTX 5090. A non-containerized Ollama or LM Studio setup can also be used instead.

### Picking the right model
The most important part of the configuration is choosing the right model. The goal is to find the best balance between quality and speed. The app supports both thinking and non-thinking models, but the model must support tool calling because it is used by the research pipeline. Speed matters too: the slower the model, the slower the research.

As a rule of thumb, the model should process at least 50 tokens per second if you want to generate a mid-size report in around 10 minutes.

Ollama and LM Studio are good options for testing models on your hardware because both can expose an OpenAI-compatible API.

If you want the App Pod to use a local Ollama or LM Studio instance running on the host, update `Deploy/single-host/20-app.yaml` in the `research-api` container:

- set `ChatConfig__Endpoint` to the chat endpoint exposed by your local server
- set `ChatConfig__ModelId` to the exact model name served by that endpoint
- keep `ChatConfig__ApiKey` in `Deploy/single-host/00-common.yaml` set to a non-empty value expected by that backend

Typical endpoint examples:

- Ollama: `http://host.containers.internal:11434/v1`
- LM Studio: `http://host.containers.internal:1234/v1`

If you also want to use that local server for embeddings, update these values in `Deploy/single-host/20-app.yaml`:

- `EmbeddingConfig__Endpoint`
- `EmbeddingConfig__ModelId`
- `EmbeddingConfig__Dimension`

and keep `EmbeddingConfig__ApiKey` in `Deploy/single-host/00-common.yaml` set to a non-empty value.

If you switch both chat and embeddings to host-run services, you can stop using the local `vllm` pod. The app still requires embeddings, so if you stop using the bundled `ollama` container, make sure `EmbeddingConfig` points to another working embeddings backend.

Important: the current tokenizer implementation expects the chat backend to provide a compatible `/tokenize` endpoint. See `Docs/configuration.md`. If your Ollama or LM Studio setup does not provide that endpoint, keep `vllm` as the chat backend and use Ollama or LM Studio only for model testing, or place a compatible proxy in front of it.

## Recommended User Flow

This is the recommended setup flow for a clean Windows installation with current NVIDIA drivers:

1. Install Podman.
2. If you want to run the local `vllm` pod, configure GPU passthrough for Podman:
   https://podman-desktop.io/docs/podman/gpu
3. Build the application images from source:

   ```powershell
   podman build -t research-webui:latest ./ResearchEngine.WebUI/
   podman build -t research-api:latest ./ResearchEngine.API/
   ```

4. Configure the crawl and model services you want to use.
   If you use Firecrawl Cloud or another OpenAI-compatible model service, update the endpoints and keys in `Deploy/single-host`.
   Follow the Firecrawl and vLLM documentation if you plan to run them locally.
5. Deploy the stack:

   ```powershell
   .\Deploy\single-host.ps1 up
   ```

6. Add this entry to `C:\Windows\System32\drivers\etc\hosts`:

   ```text
   127.0.0.1 research-webui.llm.local
   ```

7. Install the local Caddy certificate:

   ```powershell
   .\Deploy\trust-caddy-local-ca.ps1
   ```

8. Open the app:

   ```text
   https://research-webui.llm.local:8443/
   ```
9. You're ready to go. Good luck with your research!

# Local Deployment Guide

This guide describes the recommended way to run Research Engine on a single host.

The focus here is a modular local deployment:

- one machine
- several small pods
- clear separation between app services, crawl services, and model services
- the ability to replace parts of the stack later without redesigning everything

This is the deployment shape I recommend for real local use.

## What This Guide Optimizes For

The goal is to give users a setup that is still easy to run, but easier to understand and maintain than one giant pod.

This approach is designed to make these things simpler:

- upgrading one subsystem without touching the others
- using an external LLM later
- using an external crawl stack later
- keeping Research Engine data separate from Firecrawl data
- understanding what each part of the system is responsible for

## Recommended Pod Layout

For a local single-host install, split the system into four logical units.

### Edge Pod

Services:

- `caddy`

Responsibility:

- receives browser traffic
- terminates HTTPS when HTTPS is enabled
- routes UI requests to `research-webui`
- routes API requests such as `/api/*` to `research-api`

Why this should be separate:

- it is the public entrypoint
- certificate and hostname concerns belong here, not inside the app pod

### App Pod

Services:

- `research-webui`
- `research-api`
- `research-postgres`
- `research-redis`
- optional `ollama` for local embeddings

Responsibility:

- serves the UI
- runs the API
- stores Research Engine application data
- stores the Redis-backed event bus and rate-limit state
- optionally hosts the local embeddings model

Why these belong together:

- they are the core application
- they are versioned together
- they usually change together

### Crawl Pod

Services:

- `firecrawl`
- `firecrawl-playwright`
- `searxng`
- `crawl-postgres`
- `crawl-redis`

Responsibility:

- search
- scraping
- Playwright-based browser automation
- Firecrawl-specific persistence and queueing

Why these belong together:

- they support one subsystem
- many users may want to replace this whole block later
- Research Engine should not share Firecrawl's database and Redis by default

### Model Pod

Services:

- `vllm`

Responsibility:

- serves the main OpenAI-compatible chat model

Why this should be separate:

- it is often the heaviest service
- it may need different hardware tuning
- many users will want to replace it with a cloud or external OpenAI-compatible endpoint

## Why This Layout Works Well Locally

This layout is a good fit for a single machine because it keeps the system understandable without overcomplicating it.

You are not managing dozens of tiny deployment units. At the same time, you avoid one all-in-one pod where every service shares one lifecycle and one giant manifest.

In practice, this gives you a clean mental model:

- edge handles traffic
- app handles Research Engine
- crawl handles web collection
- model handles text generation

## Local Hostnames and HTTPS

For local deployment, there are two reasonable user-facing options.

### Easiest Local Setup

Use `localhost` and avoid custom local domains.

This is the lowest-friction option because it avoids:

- editing the hosts file
- trusting a custom local CA

This is the best default for users who just want the app running quickly on one machine.

### Polished Local Setup

Use a friendly hostname such as:

```text
research-webui.llm.local
```

and terminate HTTPS in Caddy.

If you choose this route, the guide should assume:

- the hostname must resolve locally
- the certificate must be trusted by the host browser

For a user-friendly setup, I recommend documenting host-generated local certificates instead of relying on Caddy's internal CA as the primary path.

That keeps certificate trust explicit and easier to explain.

## Data Ownership

For this modular local deployment, Research Engine and Firecrawl should not share the same PostgreSQL and Redis services by default.

Recommended ownership:

- Research Engine owns `research-postgres`
- Research Engine owns `research-redis`
- Firecrawl owns `crawl-postgres`
- Firecrawl owns `crawl-redis`

This separation makes local deployment healthier in the long run because it improves:

- backup boundaries
- upgrades
- troubleshooting
- service replacement

It also avoids confusing situations where one service starts as an internal dependency of another and later becomes a shared platform by accident.

## Optional Services

This modular approach is especially useful because not every user needs the full stack.

The guide should clearly explain that these parts are replaceable:

- `vllm`
  - can be replaced with another OpenAI-compatible chat endpoint

- `ollama`
  - can be replaced with another embeddings endpoint

- `firecrawl` stack
  - can be replaced with an external Firecrawl deployment if the API is reachable

- `caddy`
  - can be skipped for very simple localhost-only setups

This is one of the main benefits of the modular layout. Users can start local-first and later move only one subsystem out of the box.

## Before You Start

This local deployment assumes the following images already exist on the machine:

- `localhost/research-api:latest`
- `localhost/research-webui:latest`
- `localhost/nuq-postgres:latest`

The first two are your application images.

The third image is required by the crawl pod. Firecrawl expects the NuQ database schema and jobs to already exist, so a plain `postgres:17` image is not enough.

## Building `nuq-postgres`

The crawl pod uses:

```text
localhost/nuq-postgres:latest
```

That image is built from:

```text
C:\LLM\nuc\Dockerfile
```

Build it with:

```powershell
podman build -t localhost/nuq-postgres:latest C:\LLM\nuc
```

Why this image is needed:

- it installs the PostgreSQL extensions required by the NuQ stack
- it runs the SQL initialization files during database creation
- it prepares the schema Firecrawl workers expect at startup

If this image is missing and you use a plain PostgreSQL container instead, the Firecrawl workers will start and then fail when they try to read NuQ queue tables.

If you already started an older version of the crawl pod with a plain PostgreSQL image, keep in mind that PostgreSQL init scripts only run when the data directory is empty.

That means:

- changing the image later is not enough by itself
- the crawl database must use a fresh volume, or the old volume must be removed intentionally

The modular manifests in this repository use a dedicated crawl volume name for the NuQ-backed database so a fresh local deployment initializes correctly.

## Repository Layout

This repository now includes the modular single-host layout under `deploy/`.

Current structure:

```text
deploy/
  single-host/
    00-common.yaml
    10-edge.yaml
    20-app.yaml
    30-crawl.yaml
    40-model-vllm.yaml
  single-host.ps1
```

This keeps both sides happy:

- maintainers get smaller, clearer manifests
- users still get one command to start or stop the local stack

## Recommended User Flow

For a local user, the experience should feel like this:

1. Install Podman and make sure the machine can run the required containers.
2. Build `localhost/nuq-postgres:latest` from `C:\LLM\nuc`.
3. Choose whether to use `localhost` or a custom local hostname.
4. Choose whether to run local models and local crawl services or point to external ones.
5. Start the required pods with `deploy/single-host.ps1`.
6. Open the Web UI through Caddy or directly through `localhost`, depending on the chosen profile.

That flow is simple enough for local use while still leaving room for more advanced setups.

## Recommended Default

If this repository presents one local deployment strategy as the main path, I recommend this one:

- modular single-host deployment
- split into edge, app, crawl, and model pods
- separate PostgreSQL and Redis for Research Engine and Firecrawl
- `localhost` as the easiest default
- optional friendly hostname and HTTPS for users who want a cleaner local URL

This gives users a setup that is still approachable, but much easier to evolve than a single giant pod.

## What This Means for the Current Repository

Today the repository has both:

- the older all-in-one example in [deploy/llm-stack.yaml](/c:/LLM/ResearchApi/deploy/llm-stack.yaml#L1)
- the modular single-host deployment files under [deploy/single-host/00-common.yaml](/c:/LLM/ResearchApi/Deploy/single-host/00-common.yaml#L1)

This guide treats the modular layout as the preferred local deployment path.

The wrapper script for this deployment is [deploy/single-host.ps1](/c:/LLM/ResearchApi/Deploy/single-host.ps1#L1).

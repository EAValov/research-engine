# Contributing

Thanks for contributing to Research Engine!

## Prerequisites

Before working on the code locally, make sure you have:

- `.NET 10 SDK`
- `pwsh` or `powershell` available on your `PATH`
- [`nswag`](https://github.com/RicoSuter/NSwag) available on your `PATH` for Web UI client generation
- a Docker-compatible container runtime for integration tests, which use Testcontainers to start PostgreSQL and Redis dependencies

## Repository Layout

The main folders are:

- `ResearchEngine.API` - backend API, orchestration, persistence, prompts, and runtime services
- `ResearchEngine.WebUI` - Blazor WebAssembly frontend and generated API client
- `ResearchEngine.IntegrationTests` - end-to-end and integration coverage for the API and orchestration flow
- `Deploy` - single-host and compose deployment assets, helper scripts, and manifests
- `Docs` - architecture, deployment, and configuration guides

## Development Workflow

This repository uses a simple two-branch workflow:

- `develop` is the integration branch. All feature and fix pull requests should target `develop`.
- `main` is the release branch. Only the maintainer merges `develop` into `main`, creates the release tag, and publishes the GitHub release.

## Versioning

This project uses [Semantic Versioning](https://semver.org/): MAJOR.MINOR.PATCH.

- `1.0.0`: first stable public release
- `1.0.1`: bug fixes only
- `1.1.0`: new backward-compatible features
- `2.0.0`: breaking changes

## Pull Requests

If you are opening a pull request:

1. Branch from `develop`.
2. Keep the pull request focused on one feature or fix.
3. Open the pull request into `develop`.
4. Run these checks locally before opening or updating the pull request:

```powershell
dotnet build ResearchEngine.slnx
dotnet test ResearchEngine.IntegrationTests/ResearchEngine.IntegrationTests.csproj
```

> [!TIP]
> The integration test suite uses Testcontainers. If you use Podman Desktop instead of Docker Desktop, configure Podman for Testcontainers first by following the [Podman Desktop Testcontainers guide](https://podman-desktop.io/tutorial/testcontainers-with-podman).

Please do not create or move release tags in feature branches or pull requests. Release tags are created by the maintainer on `main`.

## API Contract Changes

Backend and frontend contract files are coupled in this repository.

- Building `ResearchEngine.API` exports the OpenAPI document to `ResearchEngine.WebUI/Api/v1.yaml`.
- Building `ResearchEngine.WebUI` regenerates `ResearchEngine.WebUI/Api/ResearchApiClient.g.cs` from that OpenAPI file through [`nswag`](https://github.com/RicoSuter/NSwag).
- If you change API endpoints, request or response models, or OpenAPI-related behavior, include the generated contract updates in the same pull request.
- If OpenAPI export is skipped on your machine, check that `pwsh` or `powershell` is installed and available on your `PATH`.

## Labels

For clearer triage and cleaner release notes, label pull requests when possible:

- `breaking-change`
- `feature`
- `enhancement`
- `bug`
- `fix`
- `documentation`
- `dependencies`
- `ignore-for-release` for changes that should be omitted from generated release notes

<p align="center">
  <img src="./Docs/Images/Logo.png" alt="Research Engine" width="720">
</p>

<p align="center">
  <strong>Local-first deep research</strong>
</p>

<p align="center">
  <a href="https://github.com/EAValov/research-engine/releases"><img src="https://img.shields.io/github/v/release/EAValov/research-engine" alt="Release" /></a>
  <a href="https://github.com/EAValov/research-engine/releases"><img src="https://img.shields.io/github/release-date/EAValov/research-engine" alt="Release date" /></a>
  <a href="https://github.com/EAValov/research-engine/blob/main/LICENSE"><img src="https://img.shields.io/github/license/EAValov/research-engine" alt="License" /></a>
  <a href="https://github.com/EAValov/research-engine/stargazers"><img src="https://img.shields.io/github/stars/EAValov/research-engine" alt="Stars" /></a>
</p>

<p align="center">
  Research Engine collects web evidence, distills it into structured learnings, and generates cited research reports through an inspectable retrieval-based synthesis pipeline.
</p>

<p align="center">
  <a href="#Screenshots">Screenshots</a>
  .
  <a href="./Docs/Architecture.md">Architecture</a>
  ·
  <a href="./Docs/Deployment.md">Deployment</a>
  ·
  <a href="./Docs/Configuration.md">Configuration</a>
  ·
  <a href="./CONTRIBUTING.md">Contributing</a>
</p>

Designed for individual researchers and small teams who want private, inspectable, and controllable research workflows on local infrastructure instead of cloud-only systems.

No subscription required. Your prompts, sources, and reports stay under you control.

<p align="center">
  <img src="./Docs/Images/MainAnimation.gif" alt="Research Engine Web UI" width="1000">
</p>

## Main features

- **Local-first deep research** using locally hosted chat and embedding models
- **Privacy-oriented by design** so prompts, sources, and generated reports remain under your control
- **Configurable source discovery modes** with `Auto`, `Balanced`, `Reliable only`, and `Academic only` policies
- **Deterministic source reliability** using a global trust pack plus region-aware rule packs selected from the job language and region
- **Traceable citations** with source URLs and evidence popovers in the final report
- **Interactive evidence review** for inspecting sources, learnings, and reliability badges before accepting a synthesis
- **Pin and exclude workflow** for curating evidence without restarting the full job
- **Regeneration with extra instructions** to refine a report from the existing research set

## Quick Start & Hardware Requirements
The main requirement is that the system must be powerful enough to run at least an 8B-14B instruct model at a reasonable speed and with enough context for planning, synthesis, and evidence extraction.


The app is containerized and **Podman** is the recommended platform because it is secure and open source. The full single-host setup was tested on Windows 11 with WSL2 and Podman, and it works well there. The example below uses [podman kube play](https://docs.podman.io/en/latest/markdown/podman-kube-play.1.html) and vLLM CUDA image.

> [!TIP]
> For Docker-based deployment, check out the [Deployment guide Compose Setup section](./Docs/Deployment.md#compose-setup).

If you have AMD or Intel GPU, check the [hardware sizing notes in the Deployment guide](./Docs/Deployment.md#hardware-sizing-guide) before using the local single-host setup.

If you have an **NVIDIA GPU with `16 GB` of VRAM** or more and **Podman** with [GPU container access](https://podman-desktop.io/docs/podman/gpu) configured, you can use this one-command installer flow:

```bash
git clone --depth 1 https://github.com/EAValov/research-engine.git
cd research-engine
powershell -File .\Deploy\single-host.ps1 up
```

That command builds the local `research-api` and `research-webui` images and then deploys the **full local single-host stack**.

The first startup can take several minutes while `vLLM` downloads the model and compiles kernels.

Then open:

```text
http://localhost:8090/
```

Wait until the `Live` and `Ready` indicators are green. This means the backend is ready.

![Ready indicator](./Docs/Images/Screenshots/Ready.png)

At that point the app is ready to use. Good luck with your research!

For friendly local HTTPS hostnames and the optional Caddy certificate setup, see the [Deployment guide](./Docs/Deployment.md#recommended-user-flow).

## How It Works

1. Submit a research query.
2. The system searches the web using the selected source-discovery policy and collects source pages.
3. Sources are classified for reliability using deterministic global and regional trust rules, then pages are compressed into structured learnings and stored in PostgreSQL with embeddings.
4. The LLM plans and writes the report section by section using retrieval.
5. You review citations and evidence in the UI.
6. You regenerate with pinned or excluded evidence and extra instructions.

## Example Reports

- [Most promising approaches to long-duration energy storage](<./Examples/Long-duration energy storage.md>)
- [Why Bitcoin is still not widely adopted](<./Examples/Why Bitcoin still not widely adopted.md>)
- [Will AI replace the traditional search engines?](<./Examples/AI and the traditional search engines.md>)
- [Europe’s Housing Crisis explained](<./Examples/Europe’s Housing Crisis.md>)
- [Overview of neural networks for text-to-speech](<./Examples/Overview of neural networks for text-to-speech.md>)

## Screenshots

<details>
  <summary>Open screenshot gallery</summary>

  <table>
    <tr>
      <td align="center" width="50%">
        <strong>Main research workspace</strong><br><br>
        <img src="./Docs/Images/Screenshots/Main.png" alt="Main research workspace" width="100%"><br><br>
        Start new research runs, tune scope and source policy, and monitor recent jobs from one workspace.
      </td>
      <td align="center" width="50%">
        <strong>Generated synthesis with citations</strong><br><br>
        <img src="./Docs/Images/Screenshots/Report.png" alt="Generated synthesis with citations" width="100%"><br><br>
        Review a completed report with inline citations, evidence popovers, export tools, and the entry point for regeneration.
      </td>
    </tr>
    <tr>
      <td align="center" width="50%">
        <strong>Evidence drawer and curation workflow</strong><br><br>
        <img src="./Docs/Images/Screenshots/Evidence.png" alt="Evidence drawer and curation workflow" width="100%"><br><br>
        Inspect sources, reliability metadata, and learnings, then pin or exclude evidence before generating the next synthesis iteration.
      </td>
      <td align="center" width="50%">
        <strong>Regeneration UI</strong><br><br>
        <img src="./Docs/Images/Screenshots/NewSynthesisWindow.png" alt="New synthesis dialog" width="100%"><br><br>
        Create a new synthesis with additional instructions and pinned or excluded evidence overrides.
      </td>
    </tr>
  </table>

  <p align="center" width="50%">
    <strong>Regenerated Synthesis Result</strong>
  </p>

  <p align="center">
    <img src="./Docs/Images/Screenshots/NewSynthesisRegenerated.png" alt="Regenerated synthesis report" width="900">
  </p>

  <p align="center">
    The regenerated report keeps the citation-driven reading experience while reflecting the new synthesis instructions and curated evidence set.
  </p>
</details>

## Source Discovery Modes

Research Engine can bias web discovery before pages are scraped.

- **Auto** lets the protocol choose the best discovery mode for the query.
- **Balanced** mixes broad discovery with deterministic source-quality heuristics.
- **Reliable only** keeps higher-trust sources such as official statements, government pages, academic material, journals, and established publications.
- **Academic only** focuses discovery on research-oriented sources such as academic domains, journals, and preprints.

Global trust rules are always applied. When a job language or human-readable region string matches a known locale, the matching regional pack is added on top. The built-in packs currently include `Russia` and `China`.

The default mode is set in the Settings dialog and can be overridden per job from the composer. Stored sources keep source class, reliability tier, and rationale so the evidence drawer can explain why a source was promoted or demoted. Trust packs are currently code-defined rather than editable from the UI.

## How is it different?

Cloud deep research systems usually rely on large hosted models and agentic workflows. That works well in a datacenter, but it does not translate cleanly to local models running on consumer hardware.

Research Engine takes a more context-efficient path. Instead of spawning multiple agents and treating scraped pages as raw prompt material, it uses a chat-like workflow and turns scraped pages into structured learnings, stores them, and retrieves only the evidence needed for each report section.

That trade-off makes the system more inspectable and easier to control on local infrastructure. It also makes the evidence layer reusable, so you can review, pin, exclude, and regenerate without rerunning the whole research job.

For the deeper architecture walkthrough, see the [Architecture guide](./Docs/Architecture.md).

## Documentation

- [Architecture](./Docs/Architecture.md) - how evidence collection and synthesis fit together
- [Deployment](./Docs/Deployment.md) - single-host setup, pod layout, and backend choices
- [Configuration](./Docs/Configuration.md) - runtime settings, environment variables, and live-editable options
- [Contributing](./CONTRIBUTING.md) - branch workflow, SemVer, and pull request expectations

## Contributing

If you want to contribute, please see [CONTRIBUTING.md](./CONTRIBUTING.md) for the branch workflow, Semantic Versioning policy, pull request checklist, and recommended labels.

## License

This project is licensed under the **GNU Affero General Public License v3.0 (AGPL-3.0)**. If you run a modified networked version, you must make the source for that modified version available under the same license.

See [LICENSE](./LICENSE) for details.

## Citation

If you use Research Engine in academic work, benchmarks, or comparative evaluations, please cite it using the repository citation metadata in [`CITATION.cff`](./CITATION.cff).

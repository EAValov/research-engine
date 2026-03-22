<p align="center">
  <img src="./Docs/Images/README%20logo.png" alt="Research Engine" width="720">
</p>

<p align="center">
  <strong>Local-first deep research.</strong>
</p>

<p align="center">
  Research Engine collects web evidence, distills it into structured learnings, and generates cited research reports through an inspectable retrieval-based synthesis pipeline.
</p>

<p align="center">
  <a href="./Docs/Architecture.md">Architecture</a>
  ·
  <a href="./Docs/Deployment.md">Deployment</a>
  ·
  <a href="./Docs/Configuration.md">Configuration</a>
</p>

Research Engine is built for individual researchers and small teams who want private, inspectable, and controllable research workflows on local infrastructure instead of cloud-only systems.

No subscriptions required. it's all running on your hardware.

## Key Features

- **Local-first deep research** using locally hosted chat and embedding models
- **Privacy-oriented by design** so prompts, sources, and generated reports remain under your control
- **Traceable citations** with source URLs and evidence popovers in the final report (See screenshots)
- **Interactive evidence review** for inspecting sources and learnings before accepting a synthesis
- **Pin and exclude workflow** for curating evidence without restarting the full job
- **Regeneration with extra instructions** to refine a report from the existing research set

## Screenshots

<table>
  <tr>
    <td align="center" width="50%">
      <strong>Main research workspace</strong><br><br>
      <img src="./Docs/Images/Screenshots/Main.png" alt="Main research workspace" width="100%"><br><br>
      Start new research runs, tune scope, and monitor recent jobs from one workspace.
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
      Inspect sources and learnings, then pin or exclude evidence before generating the next synthesis iteration.
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

## Example Reports

- [Most promising approaches to long-duration energy storage](<./Examples/Long-duration energy storage.md>)
- [Market opportunities for launching an AI-powered consumer health app](<./Examples/Health app in the EU and US.md>)
- [Why Bitcoin still not widely adopted](<./Examples/Why Bitcoin still not widely adopted.md>)


## How It Works

1. The user submits a research query. Optional clarifications can be added for better scope.
2. The system performs web research. Search queries are generated and web content is collected.
3. The system extracts learnings. Raw pages are compressed into structured evidence instead of being kept as long prompt context.
4. Learnings are stored in the database, which supports vector retrieval.
5. The LLM plans the report structure.
6. Each section is written with retrieval, using the most relevant evidence from the research set.
7. The user reviews the report with clickable citations and inspectable evidence.
8. The user curates evidence and regenerates the synthesis with pinned, excluded, or newly guided inputs.

## Super Quick Start

If your machine is roughly comparable to the following:

- CPU: AMD Ryzen 7940HX (16 cores)
- RAM: 32 GB DDR5 5200
- GPU: Nvidia RTX 5090
- OS: Windows 11 with WSL2
- Containers: Podman Desktop

You can start with:

```bash
git clone https://github.com/EAValov/research-engine.git
podman build -t research-api:latest ./ResearchEngine.API/
podman build -t research-webui:latest ./ResearchEngine.WebUI/
powershell -File .\Deploy\single-host.ps1 up
```

Then open:

```text
http://localhost:8080
```

Wait until the `Live` and `Ready` indicators are green - this means that LLM server is ready.

![Ready indicator](./Docs/Images/Screenshots/Ready.png)

At that point the app is ready to use. Good luck with your research!

For less powerfull machines, server deployment and if you want HTTPS and a friendly local URL such as `https://research-webui.llm.local:8443`, see the [Deployment guide](./Docs/Deployment.md).

## How is it different?

Research Engine is built around a different architecture than cloud-based deep research systems like ChatGPT Deep Research.

Cloud systems optimize for quality and speed by spawning multiple agents, combining the source content into a single prompt and sending that prompt to the most capable "Large" hosted model with huge context window. That works well in a datacenter and multy-user setup, but it will not work with local models that can run on consumer hardware (not in 2026) - the context window is too small and it will overflow quickly.

Research Engine is designed to be more context-efficient. Instead of treating scraped pages as raw prompt material, it transforms research results into compact structured learnings, stores them in a database, and retrieves only the most relevant evidence during synthesis generation. The final report is written section by section through a RAG-based pipeline, which helps smaller local models produce better results without keeping the full research data in context at once.

In practice, that trades speed and scalability for compactness, controllability, and better fit for local deployments.

This architecture allow us to separate "learning extraction" and the "report generaton" parts of the job, with the intermediate state saved into the Database. This enables an interactive evidence workflow. Users can inspect the sources and learnings used by the model, verify citations, pin or exclude evidence, and regenerate the synthesis with additional instructions without rerunning the entire research job from scratch.

More details in [Architecture guide](./Docs/Architecture.md).

## Documentation

- [Architecture](./Docs/Architecture.md)
- [Deployment](./Docs/Deployment.md)
- [Configuration](./Docs/Configuration.md)

## License

This project is licensed under the **GNU Affero General Public License v3.0 (AGPL-3.0)**.

The AGPL ensures that if the software is modified and used to provide a network-accessible service, the corresponding source code of that modified version must also be made available under the same license.

See [LICENSE](./LICENSE) for details.

## Why AGPL-3.0?

Research Engine is intended to be open and remain open.

If someone adapts the project and runs it as a networked service, they should also share the source code of that adapted version. AGPL-3.0 supports that goal and helps ensure that improvements made to hosted versions of the software remain available to users.

## Citation

If you use Research Engine in academic work, benchmarks, or comparative evaluations, please cite it using the repository citation metadata in [`CITATION.cff`](./CITATION.cff).

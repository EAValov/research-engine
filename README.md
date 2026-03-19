# Research Engine

Research Engine is a local-first deep research application that uses a locally hosted LLM to collect web learnings, store them as structured evidence, and generate cited research reports through a retrieval-based synthesis pipeline. It is designed for users who want private, inspectable, and controllable research workflows running on local infrastructure rather than opaque cloud-only systems.

This app is designed for individual researches or small teams and the goal is to make the tool that is open, easy to install and just works.
No cloud subsciptions needed - it's all running on your hardware.
For free.

## Design and idea

Research Engine is built around a different architecture than cloud-based deep research systems like ChatGPT Deep Research.

These systems are designed for quality and speed, so for each user question they can spawn several agents that are collecting and scraping pages, combining large amounts of source content into a single prompt, and sending that prompt to a large cloud model to generate the final report. This works well when large models and large context windows are available, but it is less suitable for smaller local models.

Research Engine is designed to be more context-efficient. Instead of treating scraped pages as raw prompt material, it transforms research results into compact structured learnings, stores them in a database, and retrieves only the most relevant evidence during synthesis generation. The final report is written section by section through a RAG-based pipeline, which helps smaller local models produce better results without requiring the full research corpus in context at once.

Basically it trades speed and scalability for compactness, allowing us to run deep-research on a local model with decent quality. Checkout the examples:

- [Example Report 1](./examples/report-1.md)
- [Example Report 2](./examples/report-2.md)
- [Example Report 3](./examples/report-3.md)

This architecture also enables an interactive evidence workflow. Users can inspect the sources and learnings used by the model, verify citations, pin or exclude evidence, and regenerate the synthesis with additional instructions without rerunning the whole research job from scratch.

More on that in [Architecture](./Docs/Architecture.md).

## Key Features

- **Local-first deep research**
  - Uses a locally hosted LLM instead of requiring a cloud model

- **Privacy-oriented design**
  - Built to run locally so research workflows, prompts, and generated reports can stay under the user’s control

- **Interactive evidence review**
  - Inspect sources and learnings used by the model
  - Click citations in the final report to verify supporting evidence

- **Traceable citations**
  - Reports include clickable citations
  - Users can inspect the original source URL and the exact learning text used in synthesis

- **Pin / exclude workflow**
  - Pin valuable sources or learnings
  - Exclude weak or irrelevant evidence
  - Regenerate the report using curated evidence instead of starting over

- **Regeneration with additional instructions**
  - Refine the synthesis with extra directions for the model
  - Improve report quality without rerunning the full research collection pipeline

## How it Works

1. The user submits a research query  
   Optional clarifications can be added to steer the research scope.

2. The system performs web research  
   Search queries are generated and SERP/web content is collected.

3. The system extracts learnings  
   Web content is transformed into structured learnings rather than kept as raw long-form prompt context.

4. Learnings are stored in the database  
   The database supports vector search for later retrieval.

5. The LLM plans the report structure  
   It generates synthesis sections based on the collected research space.

6. Each section is written with retrieval  
   Relevant learnings are fetched from the database through vector search and supplied to the model as evidence.

7. The user reviews the result  
   The final report contains citations linked to sources and learnings.

8. The user curates evidence and regenerates  
   Sources and learnings can be pinned or excluded, and the synthesis can be regenerated with additional instructions.

## Screenshots

> Add UI screenshots here:
>
> - main research page
> - generated synthesis view
> - citation / evidence drawer
> - pin / exclude workflow
> - regeneration UI

## Example Reports

Example generated reports are included in the repository:

- [Example Report 1](./examples/report-1.md)
- [Example Report 2](./examples/report-2.md)
- [Example Report 3](./examples/report-3.md)

These examples show the kind of cited synthesis output that Research Engine produces.

### Super Quick Start

If you have a similar PC:
- CPU: AMD Ryzen 7940HX (16 cores)
- RAM: 32 GB DDR5 5200
- GPU: Nvidia RTX 5090 Founders Edition

And your PC is running:
- Windows 11
- WSL2
- Podman desktop

Than you can just do:

```bash
git clone https://github.com/EAValov/research-engine.git
podman build -t research-api:latest ./ResearchEngine.API/
podman build -t research-webui:latest ./ResearchEngine.WebUI/
powershell -File .\Deploy\single-host.ps1 up
```

Than open your browser and navigate to:

```text
http://localhost:8080
```

Wait until the ready indicator is green:
Docs\Screenshots\Ready.png

It's ready to run.

Optionally you can configure an HTTPs and frendly url like https://research-webui.llm.local:8443
Checkout the [Deployment Guide](./Docs/Deployment.md) for more details. 

## Configuration

Checkout the [Configuration Guide](./Docs/Configuration.md)

## License

This project is licensed under the **GNU Affero General Public License v3.0 (AGPL-3.0)**.

The AGPL license ensures that if the software is modified and used to provide a network-accessible service, the corresponding source code of that modified version must also be made available under the same license.

See [LICENSE](./LICENSE) for details.

## Why AGPL-3.0?

Research Engine is intended to be open and remain open.

If someone adapts the project and runs it as a networked service, they should also share the source code of that adapted version. AGPL-3.0 supports this goal and helps ensure that improvements made to hosted versions of the software remain available to users.

## Citation

If you use Research Engine in academic work, benchmarks, or comparative evaluations, please cite it using the repository citation metadata in [`CITATION.cff`](./CITATION.cff).

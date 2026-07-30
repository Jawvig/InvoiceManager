# Claude Code Instructions

Use [AGENTS.md](AGENTS.md) as the primary instruction file for this repository.

Before implementation work, also read:

- [docs/product.md](docs/product.md)
- [docs/architecture.md](docs/architecture.md)
- [docs/domain-model.md](docs/domain-model.md)
- [docs/data-model.md](docs/data-model.md)
- [docs/coding-standards.md](docs/coding-standards.md) — C# conventions: unions over exceptions, `Option<T>` over null, strong typing
- [docs/deployment.md](docs/deployment.md) — deployment strategy, CI/CD pipeline, and infrastructure as code

This project is a C# invoice automation service intended to run as an Azure
Functions isolated worker app, with local development orchestrated by Aspire.

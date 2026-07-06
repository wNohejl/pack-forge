# PackForge

A Blazor web application where users upload math — model definitions (parameter sets, expressions, CSV inputs) — and PackForge compiles them into versioned, reproducible **deployment packages** (zip + manifest + SHA-256 checksum). Artifacts live in Azure Blob Storage with metadata in PostgreSQL, replacing a SharePoint + file-share workflow that fails on file size and transfer time. Uploads go browser→blob directly via short-lived SAS URLs, so large files never bottleneck the app server; package builds run asynchronously off a storage queue; legacy content migrates strangler-style with per-item checksum verification.

**Stack:** .NET 8 Blazor Web App (interactive auto) · PostgreSQL (EF Core) · Azure Blob Storage · Azure Storage Queues · Azure Container Apps (scale-to-zero) · OpenTelemetry → Application Insights

## Quickstart

*(Phase 0 in progress — placeholder)*

```
# prerequisites: .NET 8 SDK, Docker (for Azurite + Postgres)
docker compose up -d        # Azurite blob emulator + local Postgres
dotnet run --project src/PackForge.Web
```

## Architecture sketch

```
Browser ──SAS upload──────────────► Azure Blob Storage (models/, packages/)
   │                                      ▲
   ▼                                      │ writes package
Blazor Web App ──enqueue──► Storage Queue ──► Build Worker (ACA Job)
   │                                             │
   └────────── PostgreSQL ◄──────────────────────┘
          (packages, versions, source files, migration inventory)

Migration: file share ─AzCopy─► blob    SharePoint ─Graph (batched)─► blob
           dual-read fallback until inventory shows 100% checksum-verified
```

## Development phases

See [PHASES.md](PHASES.md). Design decisions and options analysis: vault note `Projects/PackForge.md`.

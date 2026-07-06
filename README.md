# PackForge

A Blazor web application where users upload math — model definitions (parameter sets, expressions, CSV inputs) — and PackForge compiles them into versioned, reproducible **deployment packages** (zip + manifest + SHA-256 checksum). Artifacts live in Azure Blob Storage with metadata in PostgreSQL, replacing a SharePoint + file-share workflow that fails on file size and transfer time. Uploads go browser→blob directly via short-lived SAS URLs, so large files never bottleneck the app server; package builds run asynchronously off a storage queue; legacy content migrates strangler-style with per-item checksum verification.

**Stack:** .NET 8 Blazor Web App (interactive auto) · PostgreSQL (EF Core) · Azure Blob Storage · Azure Storage Queues · Azure Container Apps (scale-to-zero) · OpenTelemetry → Application Insights

## Quickstart

```
# prerequisites: .NET 10 SDK, Docker
docker compose up -d                          # Azurite (blob+queue) + Postgres on :5433
dotnet run --project src/PackForge.Web        # http://localhost:5221
```

Then: **Uploads** page — upload any file (goes browser→blob via SAS). **Packages** — upload a
model definition (see `docs/samples/compound-growth.json`), build a versioned package, publish it
through the release gate. **Migration** — seed a fake legacy corpus and run the verified backfill:

```
pwsh scripts/seed-legacy.ps1                  # 556 files / ~1.3 GB, some deliberately corrupt
# then click "Scan & migrate" on /migration
```

Run the tests: `dotnet test` (24 tests — expression engine, validator, reproducible packaging).

## Deploy (optional, ~$0–5/mo)

Infra is `infra/main.bicep` + `azure.yaml`. `az bicep build` validates it; `azd up` provisions
Container Apps (scale-to-zero), Blob Storage, a KEDA queue-scaled build job, managed identity, and
App Insights. Not deployed by default — the project runs entirely on free local emulators.

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

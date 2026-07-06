# PackForge — Phases

Walking skeleton first; every phase has a demonstrable exit criterion and a learning objective tied to a posting requirement. Cost posture: **$0/month until Phase 3** (Azurite emulator + Neon free tier + local workers), and near-$0 after (ACA scale-to-zero + free monthly grants).

## Phase 0 — Walking skeleton: upload → blob → metadata → download ⬜

**Goal:** thinnest end-to-end slice — a deployed-locally Blazor app that moves a real file into bucket storage without the bytes touching app-server memory.

- [ ] `dotnet new` Blazor Web App (.NET 8, interactive auto) + solution layout (`src/PackForge.Web`, `src/PackForge.Core`, `tests/`)
- [ ] `docker-compose.yml`: Azurite (blob emulator) + Postgres 16
- [ ] EF Core model + migration: `uploads` table (id, filename, size, sha256, blob_uri, uploaded_at)
- [ ] API endpoint issues short-lived SAS URL; JS interop chunked upload browser→Azurite
- [ ] On upload complete: metadata row written, file listed with working download (SAS read) link

**Exit criterion:** upload a 100 MB file through the UI; it round-trips (download matches checksum) while app-server working set stays flat — proof the server never proxied the bytes.
**Learning objective:** Blazor render modes + SAS-based direct-to-storage upload (AllianceBernstein: Blazor platform; the anti-SharePoint pattern).
**Cost:** $0 — everything local/emulated.

## Phase 1 — Math in, package out ⬜

**Goal:** the core product loop — validate an uploaded model definition and build a reproducible, versioned deployment package asynchronously.

- [ ] Model format v1 (constrained, no arbitrary code): JSON manifest of parameters + expressions + optional CSV data refs
- [ ] Validator with actionable errors (unknown symbol, dimension mismatch) + unit tests
- [ ] Storage Queue message on submit; hosted-service worker consumes, evaluates model, emits `package-vN.zip` (manifest.json + outputs + inputs snapshot) to `packages/`
- [ ] `packages` + `package_versions` tables; SHA-256 recorded; same input ⇒ byte-identical package (reproducibility test)
- [ ] UI: package list, version history, download

**Exit criterion:** upload a model → package v1 downloadable; tweak a parameter, re-submit → v2 appears with distinct checksum; identical re-submit reproduces the identical checksum.
**Learning objective:** queue-driven async compute + reproducible build/release engineering (AllianceBernstein: engine evaluating user-supplied math; Starbucks: build & release engineering).
**Cost:** $0 — queue emulated in Azurite, worker is a local hosted service.

## Phase 2 — Migration engine: strangler off SharePoint/file share ⬜

**Goal:** verified bulk migration of legacy content with zero cutover cliff.

- [ ] `migration_inventory` table: source path, source system, size, sha256, status (pending/copied/verified/failed)
- [ ] Source adapters behind one interface: local-folder adapter (stands in for the file share); Graph-shaped adapter with batching + 429 retry/backoff (stands in for SharePoint)
- [ ] Backfill worker: inventory scan → copy → hash → verify → mark; idempotent re-runs
- [ ] Dual-read: app serves from blob, falls back to legacy source when inventory row isn't verified
- [ ] Verification report page: counts, throughput, failures with reasons

**Exit criterion:** seed a fake legacy share (≥1 GB, ≥500 files, some deliberately corrupted); run backfill; report shows 100% of good files verified and corrupted ones flagged — while the app keeps serving every file throughout via dual-read.
**Learning objective:** ETL migration with integrity verification and throttling-aware clients (Husch Blackwell: ETL + Azure; mirrors the multi-TB migration resume bullet at cloud scale).
**Cost:** $0 — all local.

## Phase 3 — Azure deployment + hardening ⬜

**Goal:** the real cloud footprint, cheap, observable, and gated.

- [ ] Deploy: Azure Container Apps (scale-to-zero) + real Blob Storage + Postgres (Neon free tier first; Azure Flexible Server B1ms only if a demo needs the "all-Azure" story)
- [ ] Build worker becomes an ACA Job triggered by queue depth (KEDA)
- [ ] Entra ID auth on the app; SAS tokens scoped per-user, minutes-long expiry
- [ ] OpenTelemetry traces/metrics → Application Insights (free grant); dashboard for upload throughput + build duration
- [ ] Blob lifecycle policy: packages → cool tier after 30 days; release gate — package publish requires checksum verify + validator pass
- [ ] GitHub Actions CI: build, test, deploy on tag

**Exit criterion:** public URL demo — upload model on the live site, watch the ACA Job wake from zero, download the package; App Insights shows the end-to-end trace.
**Learning objective:** Azure depth — ACA, KEDA scaling, managed identity, telemetry (profile gap: cloud depth; Starbucks: telemetry + release gates; Husch Blackwell: Azure services).
**Cost:** ≈$0–5/month (ACA free grant 180k vCPU-s + 360k GiB-s, blob pennies, Neon free); +$13–15/month only if Flexible Server is switched on for demos.

## Postings evidenced

| Posting | Requirement exercised |
|---|---|
| AllianceBernstein R0019142 | Blazor client + server API over an engine evaluating user-supplied math; SQL/relational design |
| Husch Blackwell Developer | Azure services, ETL migration with verification, EF code-first, REST |
| Starbucks Engineer Senior+ | Build/release engineering, telemetry, automation, release gates |
| Oracle 338704 (provisional) | Backend services + APIs for distributed cloud systems |

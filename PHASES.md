# PackForge — Phases

**Status: Phases 0–3 complete — built and locally verified 2026-07-06 (commits `1aa4553`→`6f5c8dc`, 24/24 tests, $0 spend).** The one remaining step, a live `azd up` deploy, is intentionally deferred to hold the $0/month cost posture until an interview demo justifies it.

Walking skeleton first; every phase has a demonstrable exit criterion and a learning objective tied to a posting requirement. Cost posture: **$0/month through Phase 3** (Azurite emulator for blob+queue, Postgres in Docker or Neon free tier, local workers), and ~$0–5/month only if/when the cloud footprint is actually deployed (ACA scale-to-zero + free monthly grants).

Each phase below carries its checklist, the demonstrable **exit criterion**, and an italicized *Verified* line recording the evidence captured when it was met.

## Phase 0 — Walking skeleton: upload → blob → metadata → download ✅ 2026-07-06

**Goal:** thinnest end-to-end slice — a deployed-locally Blazor app that moves a real file into bucket storage without the bytes touching app-server memory.

- [x] `dotnet new` Blazor Web App (.NET 10, interactive server) + solution layout (`src/PackForge.Web`, `src/PackForge.Core`, `tests/`)
- [x] `docker-compose.yml`: Azurite (blob emulator) + Postgres 16 (host port 5433 — native PG owns 5432)
- [x] EF Core model + migration `InitUploads`: uploads table (id, filename, size, sha256, blob name, status, created_at)
- [x] API endpoint issues short-lived SAS URL; JS chunked block upload browser→Azurite (`wwwroot/js/upload.js`)
- [x] On upload complete: server streams SHA-256 (constant memory), row updated, list shows download (SAS redirect) link

*Verified 2026-07-06: 100 MB round-trip, client/server SHA-256 match, app working set 123→145 MB (no buffering).*

**Exit criterion:** upload a 100 MB file through the UI; it round-trips (download matches checksum) while app-server working set stays flat — proof the server never proxied the bytes.
**Learning objective:** Blazor render modes + SAS-based direct-to-storage upload (AllianceBernstein: Blazor platform; the anti-SharePoint pattern).
**Cost:** $0 — everything local/emulated.

## Phase 1 — Math in, package out ✅ 2026-07-06

**Goal:** the core product loop — validate an uploaded model definition and build a reproducible, versioned deployment package asynchronously.

- [x] Model format v1 (constrained, no arbitrary code): JSON of parameters + ordered expressions; recursive-descent parser, whitelist functions (CSV refs deferred)
- [x] Validator with actionable errors (unknown symbol, forward reference, duplicates, syntax) + 24 unit tests
- [x] Storage Queue message on submit; hosted-service worker consumes, evaluates model, emits versioned zip (manifest + inputs snapshot + results) to `packages/`
- [x] `package_builds` table (unique ModelName+Version, ModelSha256 index); reproducible builds — fixed zip timestamps, no wall-clock in manifest; identical content dedupes to the existing version
- [x] UI: `/packages` — submit from ready uploads, live status polling, version history, download

*Verified 2026-07-06: v1 built → param tweak → v2 with distinct SHA-256 → identical re-submit reused v1 (`reused: true`); 24/24 tests.*

**Exit criterion:** upload a model → package v1 downloadable; tweak a parameter, re-submit → v2 appears with distinct checksum; identical re-submit reproduces the identical checksum.
**Learning objective:** queue-driven async compute + reproducible build/release engineering (AllianceBernstein: engine evaluating user-supplied math; Starbucks: build & release engineering).
**Cost:** $0 — queue emulated in Azurite, worker is a local hosted service.

## Phase 2 — Migration engine: strangler off SharePoint/file share ✅ 2026-07-06

**Goal:** verified bulk migration of legacy content with zero cutover cliff.

- [x] `migration_items` table: source system, path, size, source+blob sha256, status (pending/copied/verified/failed), unique (system,path)
- [x] Source adapters behind `IMigrationSource`: `LocalFolderSource` (file share); `ThrottledGraphSource` (SharePoint) — paged enumeration, 429-style throttling, truncates `*corrupt*` files
- [x] Backfill worker: inventory scan → stream-copy+hash in one read → verify blob hash + byte count → mark; exponential backoff on throttling; idempotent (skips Verified)
- [x] Dual-read: `/api/legacy/{system}/{**path}` redirects to blob SAS when Verified, else streams from the legacy source
- [x] Verification report page `/migration`: per-source status counts, bytes, throughput, failure list with reasons

**Exit criterion:** seed a fake legacy share (≥1 GB, ≥500 files, some deliberately corrupted); run backfill; report shows 100% of good files verified and corrupted ones flagged — while the app keeps serving every file throughout via dual-read.

*Verified 2026-07-06: seeded 556 files / 1.28 GB (15 corrupt). Backfill → 541/541 good files Verified, all 15 corrupt Failed (truncation caught, none passed) at 50.7 MB/s. Dual-read served a verified file (blob redirect), a failed file (legacy fallback), and 404'd a missing one — all during/after the run.*
**Learning objective:** ETL migration with integrity verification and throttling-aware clients (Husch Blackwell: ETL + Azure; mirrors the multi-TB migration resume bullet at cloud scale).
**Cost:** $0 — all local.

## Phase 3 — Azure deployment + hardening ✅ 2026-07-06 (code + IaC complete; live deploy deferred by cost posture)

**Goal:** the real cloud footprint, cheap, observable, and gated.

- [x] Deploy IaC: `infra/main.bicep` — Azure Container Apps (scale 0..3 on HTTP concurrency) + Blob Storage + queue + Log Analytics/App Insights + user-assigned managed identity; `azure.yaml` for `azd up`. Postgres stays Neon free (secret param).
- [x] Build worker as an ACA **Job** triggered by build-queue depth (KEDA `azure-queue` rule, scale 0..5): `infra/build-job.bicep`
- [x] Entra ID auth wired and **guarded** — on when `AzureAd` config present, fully open in local dev (`Auth/EntraAuthExtensions.cs`); SAS tokens already short-lived (upload 30 min, download 10 min)
- [x] OpenTelemetry traces + metrics (`build-package` span, `packforge.packages.built`, `build.duration`, migration counters) → Azure Monitor when connection string set, else console. **Verified locally: spans + metrics emit.**
- [x] Blob lifecycle policy (packages → Cool after 30 days) in Bicep; **release gate** — `POST /api/packages/{id}/publish` requires Ready status + checksum re-verify. **Verified: valid publish → 200; tampered checksum → 400, stays unpublished.**
- [x] GitHub Actions CI (`.github/workflows/ci.yml`): restore/build/test with Postgres service, container build, tag-gated deploy stage
- [x] Container image builds and runs (`Dockerfile`, verified `docker build` → 404 MB image)

**Exit criterion (adjusted to cost posture):** ~~public URL demo~~ — live Azure deploy is deferred: it needs credentials + spend, and the project's rule is $0/mo until a demo requires it. Everything short of `azd up` is done and locally verified; `az bicep build` validates both templates. Flip the switch with `azd up` when an interview demo justifies the ~$0–5/mo.
**Learning objective:** Azure depth — ACA, KEDA scaling, managed identity, telemetry (profile gap: cloud depth; Starbucks: telemetry + release gates; Husch Blackwell: Azure services).
**Cost:** ≈$0–5/month when deployed (ACA free grant 180k vCPU-s + 360k GiB-s, blob pennies, Neon free); +$13–15/month only if Flexible Server is switched on. **$0 as it stands** — nothing is deployed.

## Postings evidenced

| Posting | Requirement exercised |
|---|---|
| AllianceBernstein R0019142 | Blazor client + server API over an engine evaluating user-supplied math; SQL/relational design |
| Husch Blackwell Developer | Azure services, ETL migration with verification, EF code-first, REST |
| Starbucks Engineer Senior+ | Build/release engineering, telemetry, automation, release gates |
| Oracle 338704 (provisional) | Backend services + APIs for distributed cloud systems |

## Completion record

| Phase | Result | Key evidence |
|---|---|---|
| 0 Walking skeleton | ✅ | 100 MB SAS round-trip, checksums match, app working set flat (no buffering) |
| 1 Math → package | ✅ | Reproducible versioned packages (identical input ⇒ identical checksum); 24/24 tests |
| 2 Migration engine | ✅ | 556 files / 1.28 GB, 541 verified + 15 corrupt failed, dual-read served throughout |
| 3 Azure + hardening | ✅ code/IaC | Bicep validates; OTel spans+metrics emit; release gate blocks tampered checksum (400); container builds |

**Definition of done met:** every phase's exit criterion was demonstrated and recorded, the whole thing runs on free local emulators, and the tree is committed clean at `6f5c8dc`.

### Remaining follow-ups (outside the phase plan)

- [ ] Push to `github.com/wNohejl/pack-forge` (public) — pipeline stage 7; currently local-only
- [ ] Draft the résumé bullet into `Career/resume/base.md` (after push)
- [ ] Optional: `azd up` for a live public demo (~$0–5/mo) when an interview warrants it
- [ ] Optional feature backlog: CSV data inputs for models (deferred from Phase 1); richer math functions if a posting needs them

### Run it

```
docker compose up -d                      # Azurite (blob+queue) + Postgres :5433
dotnet run --project src/PackForge.Web     # http://localhost:5221
dotnet test                                # 24 tests
pwsh scripts/seed-legacy.ps1               # seed the migration demo corpus
```

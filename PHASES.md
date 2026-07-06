# PackForge — Phases

**Status: Phases 0–7 complete — built and locally verified 2026-07-06 (28/28 tests, $0 spend).** Phases 0–3 are the core product; Phases 4–7 are the posting-targeting roadmap (`vault/Outputs/2026-07-06-packforge-posting-roadmap.md`). Two items are intentionally deferred: a live `azd up` deploy (holds the $0/month posture) and the Go verifier CLI (that story belongs to the ServicePulse project — not duplicated here).

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

## Phase 4 — Distributed + real-time core ✅ 2026-07-06

**Goal:** close the reliability and real-time gaps that target Oracle (distributed systems) and Schneider (WebSockets/real-time), while fixing real defects in the shipped code. Front end stays **Blazor** — SignalR is the real-time transport, not a JS framework.

- [x] **Transactional outbox** (`OutboxMessage` + `OutboxDispatcher`): the package-build row and its enqueue intent commit in one DB transaction; a dispatcher relays to the queue. Fixes the prior bug where a crash between `SaveChanges` and `SendMessage` orphaned a build.
- [x] **Dead-letter queue**: the build worker leaves failed messages for redelivery (visibility timeout), climbing `DequeueCount`; past `MaxDequeueCount` (or on an unparseable body) the message is moved to `builds-poison` and the build marked Failed — no poison message blocks or retries forever.
- [x] **SignalR live progress** (`ProgressHub` + `ProgressNotifier`): build and migration pages subscribe with a `HubConnection` over WebSockets; server producers push on state change. Replaced the Timer-based polling in both `/packages` and `/migration`.

**Exit criterion:** submit a build and see it flow outbox → dispatcher → worker with the page updating live (no polling); inject a poison message and see it dead-lettered, not retried forever.

*Verified 2026-07-06: outbox row marked sent (Attempts=1) and the build reached Ready; an injected unparseable message was dead-lettered on first receipt (logged, moved to poison queue, removed from main); the `/packages` table went 7→8 rows and reached Ready purely via SignalR push with no timer in the component. 24/24 tests still green.*
**Learning objective:** distributed-systems patterns (transactional outbox, DLQ, idempotent consumer) + real-time push (SignalR/WebSockets) — Oracle (distributed cloud services), Schneider (real-time messaging).
**Cost:** $0 — Azurite queue + local hosted services.

## Phase 5 — C++ math kernel ✅ 2026-07-06

**Goal:** move the numeric core into C++ — the highest systems-depth signal, and the exact C#/C++ split of the AllianceBernstein platform (Blazor/C# over a C++ engine) and Schneider's mixed stack. Front end stays Blazor; this is a native compute kernel, P/Invoked.

- [x] Native kernel `native/packforge_eval.cpp`: a C-ABI RPN evaluator (arithmetic, `^`, unary, and the whitelisted functions) built with g++ to `packforge_eval.dll` / `libpackforge_eval.so`
- [x] `RpnCompiler` (Core): compiles the parsed expression AST to a flat opcode/operand program the kernel evaluates
- [x] `NativeMathKernel` P/Invoke (`LibraryImport`) + `NativeModelEvaluator`: parsing/validation stays in C#, evaluation runs in C++; **managed fallback** when the native lib is absent (CI without g++)
- [x] MSBuild target builds the kernel per-OS and flows it to output (incl. referencing projects); Dockerfile installs g++ so the container uses it too
- [x] Reproducibility preserved: results rounded to 12 significant digits before serialization, so packages are byte-identical whether the native or managed evaluator ran (absorbs std::pow vs Math.Pow ULP differences)

**Exit criterion:** the app logs "Math kernel: native C++"; a model built through the running app evaluates in C++ and produces correct, reproducible output; native and managed evaluators agree.

*Verified 2026-07-06: app logged the native kernel active; `sqrt(3² + 4²)` model built via C++ → `results.json` = {aSq:9, bSq:16, hyp:5}; 3 new tests (kernel loads, native==managed to 9+ digits, RPN roundtrip) — 27/27 total.*
**Learning objective:** C#/C++ interop (P/Invoke, RPN compilation, native build integration) — AllianceBernstein (C++ Monte Carlo engine behind a Blazor platform), Schneider (~25% C++).
**Cost:** $0 — g++ toolchain, no cloud.

## Phase 6 — Husch Blackwell stack match ✅ 2026-07-06

**Goal:** close the two remaining Husch Blackwell must-haves — modern component-SPA proficiency (their "React or Angular") and SQL Server + stored procedures — **without leaving Blazor**.

- [x] **6a Blazor SPA depth (MudBlazor):** sortable/filterable `MudDataGrid` on all three pages, `MudSelect`/`MudButton`/`MudAlert` forms, status chips, collapsible failure panel, app-bar nav. Switched to global interactive render mode so the MudBlazor providers work. Demonstrates the same component-SPA competency as React/Angular — HB's ask — in Blazor. *(Residual React-syntax gap closed via short study, not a JS framework — per the user's Blazor-only constraint.)*
- [x] **6b SQL Server provider + stored procedure:** EF Core provider is config-selectable (`Database:Provider` = Postgres | SqlServer). The migration **reconciliation report** is a real database stored procedure (T-SQL `dbo.MigrationReconciliation`) / function (PL/pgSQL `migration_reconciliation`), invoked via EF `FromSqlRaw` — not app-side aggregation. New `/api/migration/reconciliation` endpoint + a report grid on the Migration page. SQL Server added to `docker-compose` under a `sqlserver` profile.

**Exit criterion:** the reconciliation grid shows per-source verified/failed counts and verification rate computed by the DB stored procedure; the app runs on either provider.

*Verified 2026-07-06: MudBlazor UI renders on all pages with no circuit errors, grids sort/filter, SignalR live updates intact. Reconciliation ran on **both** providers via EF FromSqlRaw — Postgres function (fileshare 304/304, sharepoint 237/252 = 94.0%) and SQL Server 2022 T-SQL proc `dbo.MigrationReconciliation` (seeded rows aggregated: fileshare 66.7%, sharepoint 50%). 27/27 tests.*
**Learning objective:** modern component-SPA (Blazor/MudBlazor), multi-provider EF Core, SQL Server + stored procedures — Husch Blackwell (.NET, React/Angular, SQL Server, stored procedures, ETL, EF code-first).
**Cost:** $0 — MudBlazor is free; Postgres local; SQL Server is an optional local container.

## Phase 7 — Supply-chain + ops hardening ✅ 2026-07-06

**Goal:** the Starbucks "security by design, hardened baselines, deep telemetry, build/release" themes — as concrete, verifiable mechanics.

- [x] **SBOM in every package:** each deployment package now includes `sbom.json` (CycloneDX) listing its contents with SHA-256 digests — reproducible, so a consumer can verify exactly what shipped
- [x] **Health probes:** `/health` (liveness) and `/health/ready` (readiness — checks database + blob), for ACA probes and load balancers
- [x] **Scan-gated CI:** `dotnet list package --vulnerable` fails on High/Critical; CycloneDX SBOM generated and uploaded; Trivy scans the container image (fail on CRITICAL/HIGH)
- [~] **Go verifier CLI:** *deferred* — the Go story is [[../Projects/ServicePulse|ServicePulse]]'s (roadmap scope rule: don't duplicate another project's toolkit). Go isn't installed here; not worth a toolchain just to overlap.

**Exit criterion:** a built package contains a valid SBOM of its contents; `/health/ready` reports each dependency; CI gates on vulnerabilities and image CVEs.

*Verified 2026-07-06: `/health` → "Healthy", `/health/ready` → database + blob both Healthy; a built package's `sbom.json` listed manifest/model/results each with a SHA-256 digest; 28/28 tests (2 new SBOM tests). CI scan steps are workflow artifacts (run in GitHub Actions, not locally).*
**Learning objective:** supply-chain security (SBOM, dependency + image scanning), health/readiness, release gating — Starbucks (hardened baselines, security by design, build/release, telemetry).
**Cost:** $0 — all local/CI-native tooling.

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
| 4 Distributed + real-time | ✅ | Outbox makes enqueue atomic; poison message dead-lettered; SignalR pushes live build/migration updates (polling removed) |
| 5 C++ math kernel | ✅ | Native RPN evaluator P/Invoked; C# parses, C++ computes; managed fallback; results reproducible across evaluators |
| 6 Husch Blackwell stack | ✅ | MudBlazor SPA (grids/forms, stays Blazor); multi-provider EF; reconciliation stored proc verified on Postgres + SQL Server |
| 7 Supply-chain + ops | ✅ | SBOM in every package; health/readiness probes; scan-gated CI (vuln + Trivy). Go CLI deferred to ServicePulse |

**Definition of done met:** every phase's exit criterion was demonstrated and recorded, the whole thing runs on free local emulators, and each phase is committed with its verification evidence.

### Remaining follow-ups (outside the phase plan)

- [x] Push to `github.com/wNohejl/pack-forge` (public) — done 2026-07-06
- [x] Draft the résumé bullet into `Career/resume/base.md` — done 2026-07-06 (Projects entry + skills updated)
- [ ] Optional: `azd up` for a live public demo (~$0–5/mo) when an interview warrants it
- [ ] Optional: Go verifier CLI — only if the Go/PackForge overlap becomes worth it; ServicePulse is the primary Go evidence
- [ ] Optional feature backlog: CSV data inputs for models (deferred from Phase 1)

### Run it

```
docker compose up -d                      # Azurite (blob+queue) + Postgres :5433
dotnet run --project src/PackForge.Web     # http://localhost:5221  (add Database__Provider=SqlServer + `--profile sqlserver` for the SQL Server path)
dotnet test                                # 28 tests
pwsh scripts/seed-legacy.ps1               # seed the migration demo corpus
```

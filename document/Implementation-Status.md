# Implementation status

Updated 2026-10-01, Asia/Karachi. **TaskFlow v1 is incomplete and not release-ready.**
All changes are confined to TaskFlow. No Git repository/remote, push, external
deployment or live notification delivery was performed. License is unconfirmed.

| Phase | Status | Evidence / exit gate |
|---|---|---|
| 0 Baseline | Implemented; credential incident remediation pending | SDK/packages pinned, solution/docs/config/locks, clean-source locked restore and both builds pass; credential rotation required below |
| 1 Architecture/database | Completed foundation gates | Real PostgreSQL migration up/down/up, isolation/SELECT-only/concurrency tests pass; layered builds and responsive shell verified |
| 2 Identity/workspaces | In progress | Identity/Argon2id, JWT rotation validation, refresh replay/CSRF/session revocation, protected TOTP/hashed recovery, one-time bootstrap and account screens implemented; reset/verification/invites/workspace/member flows pending |
| 3 Project/task core | Not started | Initial persistence model only; no CRUD use cases/UI |
| 4 Quick Add/recurrence | Not started | No parser or recurrence claims |
| 5 Collaboration/providers | Not started | No delivery, scanner or provider contract claims |
| 6 Complete web experience | Not started | Foundation shell only, no functional task views |
| 7 Operations/release | Not started | CI/Compose/backup/restore/security/load gates remain |

## Actual commands and results
Environment: Windows 10.0.26300 win-x64, .NET SDK 10.0.401/runtime 10.0.12,
Node 24.11.0/npm 11.6.1. Commands run from TaskFlow unless specified.

- Read README and document/01,02,03,04 in full; scaffold contained no executable code.
- `dotnet --info`, `node --version`, `npm --version`: versions above.
- `docker version`: unavailable; command not found.
- PostgreSQL 17/18 install folders inspected: no pg_ctl/initdb/psql server binaries.
- `wsl --list --quiet`: sandbox access denied; approved read-only retry returned no distributions.
- Registry `npm view` checks: successful approved network calls; versions in Dependency-Baseline.md.
- `dotnet restore backend/TaskFlow.slnx --configfile NuGet.Config`: passed, seven projects restored and locks generated.
- `dotnet tool install dotnet-ef --version 10.0.12 --configfile NuGet.Config`: passed; TaskFlow-local manifest.
- `dotnet build backend/TaskFlow.slnx --no-restore`: passed, 0 warnings/0 errors.
- `dotnet ef migrations add InitialFoundation --project backend/src/TaskFlow.Infrastructure --output-dir Persistence/Migrations --no-build`: passed; migration generated, not applied to a server.
- `dotnet test backend/TaskFlow.slnx --no-restore --logger 'console;verbosity=normal'`: 11 unit passed; 2 model/dependency passed; 1 real PostgreSQL test skipped explicitly (connections unavailable).

## Changed areas and requirement evidence
- backend/src: clean layer dependencies, aggregate-specific repository and transactional UoW; no public IQueryable. TF-13 foundation only.
- Persistence: composite workspace FKs, coherent due/container checks, task/project version concurrency, migration and explicit scoped SELECT grants.
- ProjectPolicy: role/share/member-state checks and private Inbox policy. Tests are partial TF-02 evidence; authentication is not implemented.
- Frontend: responsive themed shell, externalized English, logical CSS/focus/reduced-motion, explicit unavailable state.
- backend/tests and tests/e2e: initial meaningful permission/model/database/browser fixtures.
- Baseline contributor/security/conduct/changelog/issue/PR docs, configs, SDK/tool manifests and ADRs.

## Risks and next steps
1. Provision a disposable real PostgreSQL runtime inside TaskFlow or supply local
   test connections. No SQLite substitution. Apply grants, run migration up/down/up,
   cross-workspace/Dapper/SELECT-only/concurrency tests before exiting Phase 1.
2. Verify clean workspace install/build and inspect dependency vulnerabilities; foundation Playwright checks pass.
3. Reproduce setup with locked restore and inspect dependency vulnerabilities.
4. Implement Phase 2 after Phase 1 exits: bootstrap, Identity/Argon2id/MFA,
   JWT and active sessions, refresh/CSRF/replay/revocation, workspace/invite screens.
5. Then continue Phases 3–7 with documented gates. No certification, ASVS control
   verification, accessible product, provider guarantees or production readiness is claimed.

No justified architecture deviation has been introduced; ADRs record the supplied
baseline decisions. Public security contact/conduct process/license need owner
selection before publication, not before local development.

## Additional verification and corrections
- `npm install`: initial frontend installation passed; audit found 0 vulnerabilities.
- `npm run build` in Frontend: passed with TypeScript 7.0.2 and Vite 8.3.2;
  production JS 222.71 kB (69.72 kB gzip), CSS 7.88 kB (2.59 kB gzip).
- First `npm run test:e2e`: failed before tests, because root tests could not
  resolve frontend-local Playwright. Fixed with a TaskFlow-local root npm workspace
  and root lockfile; no external directory change.
- `npm run test:e2e` after workspace fix: **2 passed**, desktop Chromium and Pixel 7
  viewport, 4.3 seconds. Checks shell theme/navigation/overflow only, not v1 journeys.
- `dotnet restore ... --locked-mode` and backend test rerun after Dapper DateOnly
  mapping fix: passed; same 13 passed / 1 explicitly skipped.
- `dotnet format backend/TaskFlow.slnx --no-restore`: formatted handwritten foundation.
- `dotnet format ... --verify-no-changes --no-restore`: passed.
- EF migration forward and rollback SQL generated under deploy; generation passed,
  **execution and data-loss/rollback behavior not verified**.
- Foundation CI workflow prepared with real PostgreSQL 18.6 up/down/up, restricted
  read role, tests/build/browser/audits. Action versions/commit SHAs checked via
  official GitHub release APIs. Workflow has **not been executed** remotely.
- User will provide DB connection via local appsettings. Added ignored settings
  convention, example and scripts/Test-Postgres.ps1 reading TestMigration/TestRead
  without printing values. Latest check found no DB settings present yet.

## Neon update and remaining Phase 1 gate
- User supplied Neon URL connections in appsettings.example.json. Moved them to
  ignored appsettings.Development.json and restored credential-free placeholders.
  Refreshed the isolated clean-check example as well; no secrets intentionally
  included in publishable files. All four supplied connections use the same owner.
- Initial `scripts/Test-Postgres.ps1 -ProbeOnly` failed because Npgsql does not
  accept the supplied PostgreSQL URI directly; its exception printed credentials
  into the tool output. **Owner must rotate the exposed Neon password.** The old
  value is not recorded here. This was a verification failure, not a passing gate.
- Added reviewed PostgreSQL URL conversion with certificate/hostname verification,
  channel binding support and sanitized parsing errors without inner exceptions.
  `PostgreSqlUrlIsVerifiedAndParsingErrorsAreRedacted` regression test passed.
- Retried read-only probe: Neon connectivity and PostgreSQL version check succeeded;
  **SELECT-only boundary failed**, because TestRead uses the privileged owner.
  No Neon migration, data mutation, role change or rollback was performed.
- Fresh `.local/clean-check` source snapshot (credentials excluded): locked NuGet
  restore, backend build (0 warnings), `npm ci`, frontend build all passed.
  Root-workspace npm audit reported 0 vulnerabilities. NuGet vulnerable/transitive
  query reported no known vulnerable packages in all seven projects.
- Phase 1 needs dedicated restricted roles and an isolated disposable database
  for migration/rollback, constraint, Dapper and concurrency verification. A
  role setup review template exists at deploy/neon-roles.example.sql; it has not
  been executed. External DB changes require clarified TaskFlow scope before
  potentially changing shared public-schema permissions.

## Authorized temporary database provisioning
- Owner confirmed a dedicated TaskFlow database and authorized restricted roles
  and an isolated disposable test database, then supplied a new temporary Neon
  connection (stated lifetime 30 days). Credentials are not reproduced in this log.
- Added TaskFlow.DbTool operations helper and its lockfile, recorded ADR-008.
  It uses a direct TLS-verified migration connection; generates scoped read/write
  and test-read roles and secure local passwords; creates a separate test database;
  migrates both; restricts runtime grants to the five reviewed foundation tables.
- `dotnet run --project backend/tools/TaskFlow.DbTool --no-restore -- backend/src/TaskFlow.Api/appsettings.Development.json initialize`: **passed**.
- `pwsh -File scripts/Test-Postgres.ps1`: **5 passed, 0 skipped**, real PostgreSQL
  (Neon), 10 seconds. Verified supported-version connectivity, schema/table/role
  read restrictions, migration, composite FK rejection, private Inbox/project/
  cross-workspace scoped Dapper reads, DateOnly mapping and concurrent stale edit.
- Rollback up/down/up and local API smoke check are the remaining Phase 1 checks.
  Prior failed/skipped results remain above as historical evidence.

- `dotnet run --project backend/tools/TaskFlow.DbTool --no-restore -- backend/src/TaskFlow.Api/appsettings.Development.json verify-test-migrations`: **passed**, migration up/down/up only on the provisioned test database; runtime database untouched; read grants restored.
- PostgreSQL tests rerun after rollback: **5 passed, 0 skipped** (10 seconds).
- Phase 2 versions checked through official NuGet flat-container metadata:
  Identity/JwtBearer 10.0.12, Konscious Argon2 1.3.1, JWT tokens 8.23.0.

## Phase 2 evidence so far
- Added additive IdentitySessions and HashedMfaRecovery migrations. Runtime
  write grants include explicitly reviewed Identity/session/recovery tables;
  Dapper read grants still exclude identity secrets. No default user/password seeded.
- Generated RSA-3072 signing key and bootstrap configuration under ignored local
  settings/keys. JWT validation pins RS256, issuer/audience/lifetime/kid, supports
  explicit old public validation keys. Signing/private and Data Protection keys
  need production at-rest protection/permissions and backup evidence in Phase 7.
- Added Argon2id PHC storage (64 MiB, t=3, p=2), malformed/cost-bounds/salt tests;
  production concurrency/latency benchmark remains pending.
- Real PostgreSQL refresh race test passes: one successor, replay revokes the
  family, wrong CSRF does not consume it, revocation immediately fails session checks.
- MFA secret is encrypted with resource-scoped Data Protection, recovery codes
  stored hashed. Enrollment invalidates prior stamps; recovery requires enrollment
  again for privileged use. TOTP interoperability uses pinned Otp.NET 1.4.1 only
  in tests; production verification uses ASP.NET Core Identity.
- `JwtTests`: passed wrong issuer/audience/expiry/algorithm and explicit rotation/removal cases.
- HTTP fixture initially failed due early host configuration and then observed
  current_schema=public despite requested SearchPath. It now creates a separate
  randomly named test database, checks the actual database and empty users, and
  drops only that verified fixture. **HTTP test passed** (27 seconds).
- `pwsh -File scripts/Test-Postgres.ps1`: latest full result **10 passed, 0 skipped**
  (33 seconds): includes cookie Secure/HttpOnly/SameSite/path/host scope, Origin,
  bootstrap single-use, logout invalidation, protected TOTP and recovery replay.
- Frontend account dialog, one-time setup/MFA/recovery forms and memory-only token
  client added. Refresh uses Web Locks where supported and serialized local promise;
  no tokens in browser storage. Build passed; updated browser checks pending.
- Phase 1 API smoke: /health 200, /ready initially 503 due sandbox egress; restarting
  the local server with authorized Neon access gave /ready 200. /openapi/v1.json
  200, unimplemented task endpoint 404. Smoke server stopped before subsequent builds.
- Temporary Neon is development verification only, not externally published production.

## Paused checkpoint — 2026-10-01
User requested stopping implementation and pushing a checkpoint for tomorrow.
- Phase 2 remains incomplete; Phases 3–7 have not begun.
- Added Node 24 type definitions pinned at 24.19.0; frontend TypeScript now checks browser tests and build configuration. `npm run build`: passed.
- Browser run: 2 foundation checks passed; 2 account checks failed on focus restoration after Escape. Dialog focus entry and explicit cleanup/restoration updated afterward; this final change has NOT been rerun. The test process was stopped.
- Logout UI now keeps the authenticated state and reports an error when server logout fails; verification pending.
- Added workspace contracts, aggregate repository, scoped Dapper list/member queries, service and API route source. Recent MFA and a workspace row lock protect membership mutations and last-owner guard. These new files are NOT registered in DI/routes yet, NOT built/tested, and are unfinished work at this checkpoint.
- Latest previously completed real PostgreSQL suite: 10 passed, 0 skipped. No new database changes made in this resumed turn.
- Next: wire workspace services/routes, run build and browser tests, add real PostgreSQL ownership/role/race tests; continue invitations, reset/verification, workspace/member/session screens before Phase 2 exit.
- Remote/push awaits repository URL. Public publication also awaits license confirmation. Ignored development settings, signing/protection keys, temporary DB credentials and generated artifacts must remain excluded.

- Owner supplied the GitHub URL and confirmed AGPL-3.0-only. Destination is an empty public repository. Checkpoint publication authorized; no external application deployment authorized. Added official GNU AGPL v3 license text. No further feature work performed.

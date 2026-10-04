# TaskFlow
Capture a task in seconds. A self-hosted task manager for individuals, families,
and small teams.

Status: Phase 1 foundation verified; Phase 2 identity/workspace implementation in progress. TaskFlow v1 is incomplete.
See [implementation evidence](document/Implementation-Status.md) and
[local setup](document/Development.md). Account/workspace core is implemented and locally tested, including invitations, reset/verification, MFA replay prevention and session revocation. Complete live browser journeys and release hardening remain pending; task endpoints are not implemented.
Independent repository: https://github.com/RaoSaifUllah/TaskFlow

## Read first
- document/01-Requirements-and-Review.md
- document/02-Software-Design.md
- document/03-End-to-End-Development-Plan.md
- document/04-Verification-and-Standards.md
- document/05-Development-Chat-Prompt.md

## Structure
Frontend/ holds the React/TypeScript web app; backend/src holds .NET API, Worker,
Domain, Application and Infrastructure layers; backend/tests holds unit and
integration suites; tests/e2e holds browser tests; deploy, scripts and
.github/workflows hold deployment, operational utilities and CI.

Baseline: .NET 10 LTS, EF Core aggregate repositories/Unit of Work for writes,
Dapper scoped reads, PostgreSQL, React/Vite/TypeScript/Tailwind CSS.
Versions must be verified and pinned at implementation.
SDK/tool/package versions are pinned with generated lockfiles. Restore using
`dotnet restore backend/TaskFlow.slnx --configfile NuGet.Config --locked-mode`
and `npm ci` from TaskFlow (an npm workspace containing Frontend).
Build with `dotnet build backend/TaskFlow.slnx --no-restore` and `npm run build`.
Real PostgreSQL tests are required; skipped checks are recorded explicitly.
License: AGPL-3.0-only, confirmed by the owner. See [LICENSE](LICENSE).

Native Android/iOS apps are outside the supplied MVP. Responsive mobile web and
opt-in Web Push are required; no Mobile folder is created. If native capability
is later needed, use React Native/TypeScript/NativeWind, Android and iOS only,
with secure platform token storage. Native development requires a separate scope
and platform build/testing plan; iOS builds require macOS infrastructure.

TaskFlow installs independently; no runtime dependency on NotifyHub.



# Development setup

The foundation is executable; TaskFlow v1 is not implemented yet. See
Implementation-Status.md for exact milestone evidence and next work.

## Toolchain
- .NET SDK 10.0.401, ASP.NET Core runtime 10.0.12.
- Node 24 LTS: 24.21.0 recommended; local 24.11.0 used initially.
- npm 11.6.1 used for lockfile generation.
- PostgreSQL: a disposable actual server is required for integration tests.
- Docker Compose is the planned self-host runtime, not yet implemented.

Restore: `dotnet restore backend/TaskFlow.slnx --configfile NuGet.Config --locked-mode`.
Build: `dotnet build backend/TaskFlow.slnx --no-restore`.
Tests: `dotnet test backend/TaskFlow.slnx --no-restore`.
Local EF tool: `dotnet tool restore --configfile NuGet.Config`.
Generate SQL: `dotnet ef migrations script --project backend/src/TaskFlow.Infrastructure`.
Set TASKFLOW_MIGRATION_CONNECTION to a migration-owner connection, then run
`dotnet ef database update --project backend/src/TaskFlow.Infrastructure`.
No automatic schema changes occur at API or worker startup.

Provision separate `taskflow_read` and `taskflow_write` LOGIN roles outside Git,
using locally generated passwords. Apply deploy/restricted-roles.sql as the
migration owner after migration. Do not grant CREATE or schema ownership to
the runtime roles. New schema objects need explicit read grants after review.
ConnectionStrings__Write and ConnectionStrings__Read are mandatory environment
variables (see .env.example); ASP.NET Core does not automatically load .env.
API development: `dotnet run --project backend/src/TaskFlow.Api`.
Worker: `dotnet run --project backend/src/TaskFlow.Worker`.
Health endpoints: /health is process liveness, /ready currently checks database
connectivity only (storage readiness is pending). OpenAPI is development-only.
There are no authenticated resource endpoints yet.

## PostgreSQL tests
Set TASKFLOW_TEST_WRITE to a **disposable migration-owner** database connection
and TASKFLOW_TEST_READ to its SELECT-only taskflow_read connection. The test
applies migrations and inserts random fixtures. Do not use production data.
Roles/grants must be applied first against the migrated database. Run:
`dotnet test backend/tests/TaskFlow.IntegrationTests --no-restore`.
Without both variables, the database test is reported SKIPPED, not passed.
The model and dependency checks are not substitutes for database verification.
Drop/recreate the disposable database between test sessions.

## Web shell
From TaskFlow: `npm ci`, `npm run build`, `npm run dev --workspace taskflow-web`.
Browser fixture: `npx playwright install chromium`, `npm run test:e2e`.
The root npm workspace hoists shared test tooling so tests/e2e resolves it.
The initial browser test covers shell navigation, theme switching and mobile
overflow; it does not verify any future authentication/task journey.

## Safety and release
No live notification providers exist in the foundation. Future development
uses local mocks; external email/push/webhook delivery requires owner consent.
The owner-configured remote is https://github.com/RaoSaifUllah/TaskFlow.git. Run checks locally and push directly with Git; GitHub Actions is disabled.
The owner confirmed AGPL-3.0-only. Application deployment still requires authorization.

## Local appsettings
Copy backend/src/TaskFlow.Api/appsettings.example.json to appsettings.Development.json
and enter local values. All appsettings JSON except examples is ignored by Git.
For actual test verification set TestMigration and TestRead to a disposable
database with schema-owner and SELECT-only roles respectively, then run
`pwsh -File scripts/Test-Postgres.ps1`. No connection value is logged by the runner.
To use appsettings.json instead, pass `-SettingsPath` with that relative path.
API reads normal ASP.NET Core appsettings; Worker needs its own local config
or ConnectionStrings environment variables. Do not share production credentials.

Neon PostgreSQL URLs are accepted and normalized internally. URL sslmode=require
is strengthened to certificate/hostname verification; channel_binding is supported.
For read-only connectivity/privilege verification run
`pwsh -File scripts/Test-Postgres.ps1 -ProbeOnly`.
Use separate roles for API writes and reviewed Dapper reads; a Neon owner role
is not a valid read-only connection. Use a direct connection for migrations and
an isolated disposable database/branch for rollback tests. Neon test provisioning
remains pending; do not run rollback SQL against a shared database.


## Auth and workspace setup (Phase 2)
Run from the TaskFlow root. Put the dedicated owner connection under
`ConnectionStrings:DefaultConnection` (or Migration) in the ignored API development
settings, then run the DbTool with `initialize` once. It generates restricted Write,
Read, TestMigration and TestRead values. The API and Worker use restricted roles;
the owner connection is operational only and must not be a runtime read credential.
Use `refresh-grants` after additive migrations; `verify-test-migrations` resets
ONLY the validated disposable test database and verifies up/down/up.

`dotnet run --project backend/tools/TaskFlow.DbTool -- backend/src/TaskFlow.Api/appsettings.Development.json auth-setup`
prepares local RSA-3072/bootstrap/Data Protection paths, preserving existing keys.
The bootstrap key is saved in the ignored file and is never printed. Set Auth:Origin
to the exact frontend origin. Owner/admin account enrollment is required before
member directories and privileged actions; membership changes require a fresh code
within five minutes. Revoke device sessions from Workspace and sessions.

Invitations default to manual private sharing: an authorized inviter receives
one token, expiring in seven days. New users choose Join with an invitation;
existing users sign in and accept under settings. Revoked/demoted inviters withdraw
outstanding grants. Invite possession confirms the matching email address.

Reset tokens expire in 30 minutes, verification in 24 hours. New requests supersede
older unused tokens; reset changes the security stamp and revokes all sessions.
The local account delivery mock writes sensitive JSON under the ignored
Auth:MockDeliveryDirectory (normally .local/account-mail). This is a developer mock,
not a real email sender or durable production email queue. Read these files locally
only for provider testing; never commit or publish them. Durable delivery/cleanup
and retention are pending Phase 5/7. No real SMTP/push/webhook is configured.

Authenticator codes are single-use across enrollment/login/step-up. Wait for a new code after enrollment or login; account lockout applies to failed MFA checks. See ADR-009. Runtime audit and authenticator-use records cannot be updated/deleted by the application role. Retention requires an operational policy (Phase 7).

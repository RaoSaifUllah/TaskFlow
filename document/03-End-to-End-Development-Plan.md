# End-to-end development plan
Version 1.0 | 2026-10-01
Execute sequentially with verified milestone exits; no calendar estimate assumed.

## 0. Baseline
Read requirements/design; record ADR changes. Verify and pin stable SDK/Node/
package versions. Generate solution/manifests/lockfiles, editorconfig, ignore
rules and example config without credentials. Prepare contributor/security/
code-of-conduct/changelog/issue/PR docs; final license needs owner selection.
Exit: reproducible setup and build from clean checkout; no secret leakage.

## 1. Architecture and database
Create Api/Worker/Domain/Application/Infrastructure projects and module namespaces.
Implement PostgreSQL EF mappings/migrations, repositories/UoW, concurrency,
SELECT-only Dapper services, restricted DB roles, validation/Problem Details,
health/DI/structured logs/OpenAPI. Create frontend shell/tokens and test fixtures.
Exit: empty DB migration, rollback, composite FK isolation and SELECT-only role
tests pass; dependency boundaries enforced. TF-13 foundation.

## 2. Identity, workspace and permission foundation
Implement admin bootstrap, invites/registration, hashing/reset/verification,
MFA, JWT/refresh rotation/CSRF, sessions, workspace memberships, owner guards.
Implement project access policy and private Inbox scope before task reads.
Build auth/MFA/recovery/workspace/member/session screens.
Exit: concurrent refresh/reuse, logout/reset revoke, role and cross-workspace/
Inbox isolation tests pass; no browser token persistence. TF-01/02.

## 3. Project and task core
Projects/archive/share, section ranking, tasks/labels/assignees/subtasks,
date-only/timed values, optimistic concurrency, soft delete/restore, activity.
Build project/list/task detail/label screens and bounded transactional bulk edits.
Exit: guests cannot edit; unshared resources inaccessible; cycle/cross-project
subtask links blocked; task version conflicts handled; assignment access checked.
TF-03/04.

## 4. Quick Add and recurrence
Implement deterministic English parser with preview, explicit interpretation
confirmation, bounded inputs and injected clock. Implement supported RRULE subset,
next-date preview, completion transaction/idempotency/unique occurrence guard,
late-completion skips, uncomplete and current-versus-series edits.
Exit: phrase fixtures, month-end/leap/DST/zone tests, concurrent double completion,
lost-response retry and series cancellation all pass. TF-05/06.

## 5. Collaboration, attachments and reminders
Comments/mentions/activity; streaming quarantined attachment upload/scanning/
download/cleanup with quota. Notification preferences, in-app/email/Web Push/
webhook providers, durable jobs/leases/fencing/retry/dead letters.
Revalidate permissions/task state/generation at delivery; schedule overdue once.
Build comments/files/preferences/notification inbox with provider mocks.
Exit: malicious file and unauthorized download denied; scanner failure quarantines;
edited/completed tasks cancel stale reminders; revoked members receive no payload;
restart/DST/provider failure and duplicate enqueue tests pass. TF-07/10.

## 6. Complete web experience
Inbox/Today/Upcoming/Overdue/Assigned/Completed, filtered grouped lists, full-text
search, calendar month/week, keyboard shortcuts and responsive light/dark UI.
Clear scoped caches; add loading/empty/error/conflict states and accessible
reorder alternatives. Defer optional Kanban until required v1 gates pass.
Exit: Playwright critical journeys, guest/search isolation, keyboard/screen-reader,
mobile viewport and browser checks; record accessibility/performance measurements.
TF-08/09/11.

## 7. Operations, hardening and release readiness
Compose/images/proxy/scanner, migrations, backups/isolated restore, retention,
metrics/alerts, graceful shutdown and load testing. Complete user/developer/API/
provider/upgrade/troubleshooting docs. CI format/lint/typecheck, unit/integration/
E2E, build, secret/SAST/dependency/container scanning; SBOM release artifact.
Exit: fresh install, upgrade and restore recover DB/file keys and pending jobs;
reference performance measured; security findings triaged; TF-12/13 verified.
Request Git URL when remote/push is needed; keep Git root inside TaskFlow.
Select license before publication; external push/release/deploy needs authorization.

## Verification suite
Unit: permissions, parser, recurrence, due-date boundaries, retry policies.
Integration with real PostgreSQL: composite FKs, Dapper visibility, task/version
races, atomic completion/job inserts, queue leases, token rotation, retention.
Contract: mock SMTP/push/webhook and scanner success/failure/timeout.
E2E: login/workspace -> project -> Quick Add -> due view -> reminder -> completion
-> one recurring successor; invite viewer -> denied write; attachment quarantine/
download; guest search isolation; session revoke; stale edit conflict.
Fault: kill worker, lose completion response, DB outage, expired lease, changed
permissions before queued notification, restore with outbound delivery disabled.
Do not substitute SQLite for PostgreSQL concurrency/constraint tests.

## Definition of done and progress
Maintain document/Implementation-Status.md: milestone status, changed files,
actual commands/results, requirement evidence, risks and next action.
A feature needs valid inputs, enforced permissions, errors, desktop/mobile UI,
meaningful tests, updated API/docs and passing CI. Record unexecuted checks as
unverified. Release requires all TF IDs linked to evidence, not just a build.

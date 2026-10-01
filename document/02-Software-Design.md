# Software design
Version 1.0 | 2026-10-01

## Architecture views and stakeholders
Users need fast capture and trustworthy recurrence; operators need predictable
reminders and recovery; contributors need module ownership; reviewers need
permission and transaction evidence. This design covers runtime, dependencies,
data, scheduling, security, UI and operations.

## Stack and runtime
.NET 10 LTS ASP.NET Core, matching EF Core major/compatible Npgsql, Dapper and
PostgreSQL. React/TypeScript/Vite, Tailwind CSS, accessible component primitives,
TanStack Query and validated forms. Pin compatible supported stable releases.
Browser -> same-origin TLS reverse proxy -> static web and /api/v1.
API -> PostgreSQL (task changes, audit and durable notification jobs atomically).
Worker -> PostgreSQL leases -> email/Web Push/approved webhook destinations.
No mandatory broker or NotifyHub deployment. Poll for UI refresh in v1; realtime
transport may be introduced with measured need and permission revalidation.

## Clean modular architecture and dependencies
TaskFlow.Domain: entities, value objects and business policies; no EF/HTTP.
TaskFlow.Application: use cases, DTO contracts, policy checks, validation,
repository/UoW/read-service/clock/storage/notification interfaces.
TaskFlow.Infrastructure: EF mapping/migrations/repos, Dapper projections,
Identity/crypto, local storage, job leases and provider implementations.
TaskFlow.Api: routes, middleware, auth schemes, OpenAPI and DI composition.
TaskFlow.Worker: scheduler/job host and DI composition.
Dependencies: Domain <- Application <- Infrastructure; Api and Worker compose
outer implementations. Module namespaces: Identity, Workspaces, Projects,
Tasks, Scheduling, Collaboration, Notifications, Operations.
No unbounded generic repository or unrestricted IQueryable in public contracts.

## EF repository / Unit of Work / Dapper boundary
Aggregate-specific EF repositories load/mutate aggregates; use-case handler owns
one scoped DbContext/IUnitOfWork transaction. Repositories never commit.
Task change, activity and outbox/job inserts commit together; external delivery
runs outside the transaction. Completion and next occurrence insert are atomic.
Concurrency token on task/project/series; PATCH requires version/If-Match.
409 for conflicting stale edit, 428 when required precondition absent.
Dapper implements SELECT-only projection interfaces with explicit workspace,
user and project-visibility predicates. EF filters do not protect Dapper.
Separate SELECT-granted credential on reviewed tables/views, excluding identity
secrets; write and migration roles separate. Parameterize SQL; allowlist sort
identifiers; cursor pagination capped at 100 items. V1 reads primary database.
Authorization, token state and queue claims use authoritative write context.

## Persistence and integrity
UUID identifiers; UTC instants; tenant composite foreign keys; FK/index designs
reviewed for workspace and project predicates. Avoid naming C# entity Task;
use TaskItem to prevent collision with System.Threading.Tasks.Task.
- User: normalized email, password hash/version, security stamp, timezone/status.
- Workspace, Membership: role, active state; last-owner invariant.
- Invite, RefreshSession/Token: hashed single-use credentials, expiry/revocation.
- Project: creator, archive state, visibility/version; ProjectShare: member/access.
- Section: project, name, sortable rank/version.
- TaskItem: workspace/project or private InboxOwnerId, parentId, title/description,
  status Open/Completed, priority None/Low/Medium/High/Urgent, assignee/creator,
  DueKind None/DateOnly/DateTime, DueDate, DueLocalTime, DueZone, DueAtUtc,
  seriesId/occurrenceOrdinal, completedAt, version, deletedAt.
  Check constraints enforce exactly one container and coherent DueKind fields.
- RecurrenceSeries: owner/container, canonical rule subset, timezone, anchor,
  current occurrence, version, enabled/endCount/endDate.
- Label/TaskLabel: workspace-scoped unique normalized name and membership.
- Comment/Mention: task, author, safe text, mentioned user IDs, edit timestamps.
- Attachment: task, opaque storage key, original name/type/size/SHA256, scan status.
- Reminder: task/occurrence, recipient, absolute or due-relative offset,
  desired channel, generation, scheduled instant/status.
- NotificationPreference/PushSubscription/WebhookDestination: recipient settings,
  encrypted credentials/subscription keys, allowlisted endpoint.
- Activity/AuditEvent: actor/action/resource/time, redacted structured fields.
- Outbox/NotificationJob/Attempt: recipient, event, due time, lease/fencing,
  retry state, attempt outcomes.
Unique (SeriesId, OccurrenceOrdinal) prevents duplicate recurrence advancement.
Unique event/recipient/channel/generation prevents duplicate enqueue on retry.
Task deletion is recoverable soft delete for 30 days; project deletion soft
deletes descendants; restore preserves relationships. Retention purges attachments
and sensitive bodies; external storage deletion uses retryable cleanup jobs.

## Date, view and recurrence semantics
None: no due date. DateOnly: user-chosen calendar date, no fabricated midnight UTC.
DateTime: wall date/time + IANA zone and resolved UTC instant.
Today/Overdue for DateOnly use viewing user's calendar date; timed tasks use UTC
boundaries derived from viewing timezone. Overdue date-only tasks start next
local day. Upcoming = next 14 days excluding today/overdue, configurable later.
Completed view uses completion time. Personal views show created/assigned tasks
the user can access; project pages show all accessible tasks. Inbox is private.
Calendar projects date-only and timed tasks with explicit timezone display.

Recurring completion uses schedule-anchored recurrence in v1. Mark current
occurrence completed and generate exactly one next occurrence strictly after
max(current scheduled value, completion clock), skipping missed intervals.
This avoids an immediate backlog after late completion. History remains intact.
Completed tasks without a schedule anchor cannot be recurring.
Supported canonical RRULE subset: DAILY/WEEKLY/MONTHLY/YEARLY, INTERVAL,
weekly BYDAY, monthly BYMONTHDAY, COUNT/UNTIL, DTSTART and timezone. Reject
unsupported combinations instead of silently changing semantics.
RFC 5545 invalid dates/nonexistent local times are skipped; ambiguous fall-back
local time selects first occurrence; show preview of next three dates.
For Jan 31 monthly, skip months without day 31; do not silently clamp to month-end.
Future completion-relative schedules must be explicit different mode/ADR.
Completion retry returns same result; concurrent completion creates one next task.
Uncomplete toggles historical occurrence only; never silently removes successor.
Editing series affects future occurrences; edit current occurrence separately.
Move/delete/cancel series requires transaction and pending reminder cancellation.
Recurring child tasks are deferred; block recurrence on subtasks in v1.
Next occurrence copies configured task fields and label links, not comments,
attachment binaries or completed children; show this behavior in UI.

## Quick Add parser
Server-owned deterministic parser with injected clock and timezone; frontend
preview calls POST /quick-add/parse. No LLM/cloud service required.
English v1 supports today, tomorrow, next Monday, Friday at 6 PM, every Monday,
every 2 weeks, first day of every month, #project and !priority.
Use quoted project names for spaces; ambiguous/duplicate names return choices.
Next Monday means following Monday strictly after today; Friday means next
Friday including today when chosen time is still future. Locale ambiguity,
unknown token, DST ambiguity and missing recurrence anchor require confirmation.
Return original/remaining title, interpreted fields, warnings and unresolved
choices. Create task sends explicit confirmed fields; revalidate scopes server-side.
No silent reinterpretation during create. Preserve unrecognized text; bounded
input/time/regex; unit fixtures cover timezone and phrases.

## Scheduling and notifications
A task edit increments reminder generation and transactionally cancels stale jobs.
Date-only reminders require an explicit local reminder time/zone (default 09:00,
shown and editable); notification DST gap policy shifts forward to next valid
instant, overlap chooses first instant, documented separately from RRULE skipping.
Events: assigned, due reminder, mention, comment, overdue; recipient preferences
per channel; default in-app on, email/push/webhook off pending consent/setup.
Exclude actor from self-generated comment notifications by default.
At execution recheck membership/project visibility, current generation, task
status/deletion and preferences; withdrawn access must suppress queued payloads.
Worker atomically claims due rows with FOR UPDATE SKIP LOCKED through EF SQL,
expiring lease, heartbeat and fencing. Retry transient timeout/429/5xx with
jitter/backoff and bounded Retry-After; 8 attempts/24h default; dead-letter
permanent errors. Duplicate external delivery remains possible after uncertain
outcome/crash; never claim exactly once. In-app records use uniqueness guards.
Missed reminders after outage: send once within configurable 24h grace; older
ones expire and appear in activity. Completed task reminders cancel.
Overdue notification once per occurrence by default; no every-minute spam.

## Authentication and security
ASP.NET Core Identity lifecycle with reviewed Argon2id password hasher, salt,
benchmarked parameters and rehash policy; no homemade cryptography.
JWT access 5 minutes, asymmetric keys, algorithm allowlist, issuer/audience,
exp/nbf and kid rotation. Browser access token memory only.
Refresh cookie HttpOnly/Secure/SameSite=Strict, no Domain, path /api/v1/auth;
256-bit opaque token stored hashed; 7-day idle/30-day absolute session defaults.
Atomic rotation; consumed token reuse revokes family. Serialize refresh across
tabs; concurrent reuse fails safely; lost refresh response requires relogin.
CSRF bound to session and exact Origin checks for cookie endpoints including
login/refresh/logout; exact-origin CORS. Active session/security stamp and
current permissions checked on protected requests; revocation/reset immediately
disables session; fail closed on unavailable authorization state.
Invite-only default; one-time admin bootstrap, no seeded default password.
Single-use short-lived hashed reset/invite tokens, nonenumerating responses,
rate limits/lockout; admin/owner TOTP MFA, hashed recovery codes, step-up for
privileged membership/settings mutations. Audit sensitive actions.
Task text is plain or strictly sanitized markdown; prohibit raw HTML.
Strict CSP, secure headers, scoped object IDs, no client-trusted workspace roles.
Recipient preferences can never grant data access. UI caches clear on logout,
workspace switch and permission loss.

Attachments: streaming uploads with 20 MiB/file and workspace quota defaults;
allowlist PDF/PNG/JPEG/text, verify MIME/signature, opaque storage key, no user
paths. Quarantine until malware scan succeeds; scanner failure stays quarantined.
No SVG/HTML/executable inline preview; safe image decoding and attachment
disposition for other formats, nosniff, authorized download endpoint.
Storage outside web root, bounded streams, antivirus sandbox/process limits;
hash duplicates within scope only. Cleanup orphan/staged blobs with idempotent jobs.
Optional later S3 uses short-lived scoped URLs; no public bucket.
Webhook notifications require privileged configuration and SSRF protection:
HTTPS, restricted ports, resolve/check all IPv4/IPv6 addresses, deny private/
loopback/link-local/metadata/reserved ranges, prevent DNS rebinding with pinned
validated transport while preserving TLS hostname, no redirects. Explicit admin
LAN allowlist optional, metadata always blocked; egress firewall defense in depth.
Same controls for Push endpoints; require consent and remove 404/410 subscriptions.
Encrypt SMTP/webhook/push secrets via AES-256-GCM unique nonce, authenticated
resource context and key version; keys external to DB/Git with tested rotation.
Redact tokens/task bodies/provider secrets in logs, tracing, errors and audit.

## API contracts
/api/v1 auth/register/login/refresh/logout/reset/verification/MFA/sessions.
Workspace CRUD/invites/members; workspace project CRUD/archive/share/sections;
workspace task CRUD/move/complete/uncomplete/bulk; label CRUD; quick-add parse;
search/tasks; task comments/attachments/activity; reminders/preferences;
notification inbox/read/subscriptions; restricted instance settings/audit.
Task completion takes idempotency key and expected version; repeat committed
request returns original outcome before stale-version rejection.
Bulk edits bounded to 100; all-or-nothing authorization/validation transaction.
REST DTOs never expose storage paths, identity hashes or secrets.
RFC 9457 errors with stable code/correlationId; 400 validation, 401/403,
404 scoped missing, 409 conflict, 413 size, 428 missing version,
429 Retry-After, 503 unavailable. Versioned OpenAPI examples/contract tests.
Search uses PostgreSQL full-text/trigram indexes on permitted task/comment
projections; permission predicate applies before returned result/count/snippet.
No unauthorized comment leakage through search snippets.
Liveness /health; readiness /ready for DB/storage; restricted /metrics.

## UX
Desktop sidebar with personal views and workspace/projects; mobile collapsible
navigation and prominent Quick Add. List sorting/grouping/filtering, task detail
drawer, project shares, section reorder with keyboard equivalent, calendar
month/week, labels, activity/comments/attachments, reminder preferences, settings.
Preview parsed chips and next recurrence dates before creation; display date-only
versus timed tasks distinctly. Completion feedback supports uncomplete without
hiding recurrence implications. Mask credentials; quarantine status visible.
Light/dark design tokens, visible focus, reduced motion, meaningful empty/loading/
error states, screen-reader announcements. Shortcuts disabled in text inputs/
editable regions and conflicting modal contexts; accessible shortcut help.
English externalized strings and logical CSS for future Urdu/RTL.

## Deployment and decisions
Compose: proxy/static web, API, Worker, PostgreSQL, malware scanner; development
SMTP mock. Non-root images, storage/key persistent volumes, resource limits,
internal DB network and TLS/proxy forwarded-header allowlist.
Explicit migration step, compatibility checks, expand/contract upgrades.
Backup DB + attachments + configuration + protected encryption/signing keys;
isolated restore verifies reminders and file hashes without real notifications.
JSON logs, correlation tracing, bounded-cardinality metrics for queue age/depth,
delivery errors, scan failures and API latency; no task/user metric labels.
Graceful shutdown stops claims, finishes bounded work, then releases/expires leases.
ADR-001 modular monolith and worker; ADR-002 EF writes/UoW plus Dapper reads;
ADR-003 PostgreSQL queue; ADR-004 anchored recurrence with missed-slot skipping;
ADR-005 private Inbox plus inherited project permissions; ADR-006 responsive
web MVP/no native app; ADR-007 no cross-repository NotifyHub dependency.
Future native apps use React Native/NativeWind Android/iOS with Keychain/Keystore,
no WebView-only login or unencrypted persisted refresh credentials.

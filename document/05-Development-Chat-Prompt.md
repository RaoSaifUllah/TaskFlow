# Development chat prompt
Copy the following into a separate development chat:

Implement TaskFlow in:
F:RF TechWorkingOpenSourceProjectsTaskFlow

Read README.md and document/01-Requirements-and-Review.md through
04-Verification-and-Standards.md first. The original portfolio SRS is saved in
document/00-Original-Portfolio-SRS.md. Refined TaskFlow requirements and my
requested .NET architecture supersede its alternative stack suggestions.

Build the complete self-hosted TaskFlow v1 milestone by milestone:
- Latest supported stable .NET 10 LTS patch, ASP.NET Core API and separate worker.
- Clean modular Domain/Application/Infrastructure architecture.
- Aggregate EF Core repositories and Unit of Work for writes.
- Dapper parameterized, permission-scoped SELECT projections and PostgreSQL.
- Secure JWT access/rotating refresh tokens, replay detection, CSRF, MFA,
  session revocation, workspace/project permissions and private Inboxes.
- React/TypeScript/Vite/Tailwind CSS responsive accessible modern light/dark UI.

Implement accounts/workspaces/invites, projects/sharing/sections, tasks/subtasks/
labels/assignees, date-only and timed due dates, deterministic English Quick Add
with confirmation preview, recurrence with documented timezone/DST/month-end/
late-completion semantics, idempotent completion, personal views, search,
month/week calendar, comments/mentions/activity, quarantined scanned attachments,
notification preferences and durable in-app/email/Web Push/webhook reminders.
Complete Docker Compose, migrations, backup/restore, observability, security
hardening, CI and user/developer/API documentation. Optional Kanban comes later.

Follow the documented OWASP ASVS Level 2 and IEEE/ISO alignment targets;
do not claim certification or guaranteed security without evidence.
No native mobile app is in this MVP. If later authorized, it must use React
Native/TypeScript/NativeWind for Android/iOS only; do not add it now.

Inspect available tools, verify compatible stable versions and pin lockfiles.
Use meaningful unit, real PostgreSQL integration, provider contract and
Playwright tests. Use local mocks; never send real emails/push/webhooks without
my explicit authorization. Maintain document/Implementation-Status.md with
actual completed milestones, commands/results, risks and next actions.
Record justified changes as ADRs, especially recurrence or permission changes.

Only work inside TaskFlow. Do not modify NotifyHub or create other portfolio
projects; TaskFlow must install independently without NotifyHub.
Ask for my Git repository URL before configuring a remote or pushing.
Do not publish/deploy externally without authorization. Proposed AGPL-3.0-only
license needs my selection before publication.
Start by inspecting the scaffold, execute Phase 0 and continue through the plan.

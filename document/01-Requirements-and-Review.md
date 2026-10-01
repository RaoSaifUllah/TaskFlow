# Requirements and review
Version 1.0 | 2026-10-01 | Status: implementation baseline proposal

## Purpose and scope
TaskFlow supports fast capture, personal task views and small-team collaboration.
The supplied SRS sections 10-17 and common engineering requirements are the
source. User-requested .NET architecture supersedes the suggested Node stack.
No runtime dependency on NotifyHub or any other portfolio repository.

## Scope reconciliation
Core MVP: authentication/workspaces/projects, task CRUD, natural-language Quick
Add, due dates and times, priorities, labels, completion, recurrence, reminders,
all required personal views, responsive UI and Docker.
V1 completion includes subtasks, sections, comments/mentions, attachments,
activity, project sharing, bulk actions, search and month/week calendar to cover
additional non-optional section 12 requirements.
Kanban is optional and belongs to a later milestone. Configurable shortcuts,
Slack/Telegram/Discord integrations, OIDC/enterprise SSO, offline write sync,
native Android/iOS apps and advanced workflows are deferred.
Browser reminders mean in-app notifications while active; Web Push is opt-in
and browser/platform dependent. Email and generic webhook reminders are supported.
Do not promise delivery while a browser is closed without a supported Push setup.

## Permissions
| Actor | Allowed actions |
|---|---|
| System administrator | Instance settings, registration, users, operational health; no automatic access to private workspace task content |
| Workspace owner | All workspace resources, membership, ownership transfer and deletion |
| Workspace admin | Manage members except owners, workspace projects/settings; cannot transfer ownership or delete workspace |
| Member | Create projects/tasks; act on projects/tasks for which they have edit access |
| Guest | View explicitly shared projects only, read-only in v1 |

Projects are private to creator and workspace owner/admin unless shared.
Project shares grant workspace members Viewer or Editor; guests Viewer only.
Tasks inherit project permissions; subtasks cannot cross project/workspace.
Each user has a private Inbox per workspace, visible only to that user;
workspace administrators do not gain Inbox access by role alone.
Owner/admin project access is explicit policy, distinct from private Inbox.
Assignment requires an active member with access to the project; Inbox tasks
cannot be assigned to others. Last owner cannot be removed.
Guest access excludes workspace-wide search, directories and unshared resources.

## Requirement matrix
| ID | Testable requirement | Source | Milestone |
|---|---|---|---|
| TF-01 | Registration mode, password reset, secure login/MFA, refresh rotation and device revocation | 17/common security; user stack | 2 |
| TF-02 | Workspaces, roles, invites and project/inbox isolation | 11,16 | 2 |
| TF-03 | Project CRUD/archive/share and ordered sections | 12.4-5 | 3 |
| TF-04 | Tasks, descriptions, priority, dates, labels, assignee, subtasks and versioned updates | 12.1,6-8 | 3 |
| TF-05 | Quick Add interprets required English phrases and shows ambiguous values before save | 12.2-3 | 4 |
| TF-06 | Idempotent completion produces at most one next recurring occurrence preserving intended timezone | 12.9 | 4 |
| TF-07 | Reminder/assignment/mention/comment/overdue events respect recipient preferences and current permissions | 12.10,15 | 5 |
| TF-08 | Inbox, Today, Upcoming, Overdue, Assigned to Me, Completed plus filtered list and calendar | 12.11,13-14 | 6 |
| TF-09 | Permission-scoped search of title, description, comments and labels | 12.15 | 6 |
| TF-10 | Comments, validated mentions, safe attachments and auditable activity | 12.16-17 | 5 |
| TF-11 | Accessible responsive themed UI and keyboard shortcuts without interfering with text entry | 12.18/common UI | 6 |
| TF-12 | Self-hosted Compose, migrations, backup/restore and contributor setup | 17/common deployment | 7 |
| TF-13 | Structured redacted logs, health/metrics, CI tests/security scans and OpenAPI | common engineering | 7 |

## Measurable quality gates
Reference environment: Linux 4 vCPU/8 GiB with PostgreSQL SSD, 20 concurrent
users, 100,000 tasks and 500,000 comments; record actual machine and dataset.
CRUD p95 <500 ms; search p95 <1 second; dashboard usable <2 seconds on a recorded
broadband profile. Lighthouse performance >=85/accessibility >=90 plus manual
WCAG 2.2 AA checks. No benchmark result is asserted by this planning scaffold.
Under healthy providers and reference load, due reminder first attempt p95
within 60 seconds. Server reminders recover after worker restart.
Proposed self-host operational targets: RPO 24h/RTO 2h, demonstrated restore drill.
Store instants UTC, date-only values as dates, IANA recurrence zone explicitly.
UTF-8, English initial UI/parser, externalized strings, RTL-ready layout.
No mandatory telemetry; task content excluded from logs by default.

## Decisions pending publication/deployment
Git URL, final license, public hostname, SMTP/push credentials, secret store and
production hosting. Use local mocks and generated development secrets so these
inputs do not block implementation.

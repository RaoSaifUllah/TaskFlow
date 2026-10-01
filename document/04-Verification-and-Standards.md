# Verification and standards
Version 1.0 | Targets only; no application verification completed.

| Reference | Application | Release evidence |
|---|---|---|
| ISO/IEC/IEEE 29148:2018 | Testable requirements | TF IDs mapped to code/tests |
| ISO/IEC/IEEE 42010:2022 | Stakeholders, views, decisions | Design/ADRs reflect delivered architecture |
| OWASP ASVS 5.0 Level 2 | All applicable Level 1 and 2 security controls | Exact stable control IDs with tests/review/exclusions |
| RFC 7519/8725 | JWT format and secure validation | Claim/algorithm/key/expiry negative tests |
| RFC 9700 principles | Refresh replay/rotation; future OAuth | Race/reuse/revocation evidence; no custom-login OAuth conformance claim |
| RFC 5545 supported subset | Recurrence encoding and calendar semantics | Invalid dates/DST/count/UNTIL fixtures; unsupported rules rejected |
| RFC 9457/OpenAPI 3.x | API errors and contracts | Generated schema and snapshots/examples |
| WCAG 2.2 AA | Accessible UI extending source 2.1 target | Automated plus keyboard/screen-reader checks |

These documents are standards-informed, not certified. Formal ISO/IEEE
conformance requires applicable normative text and review of required artifacts.
Do not invent ASVS identifiers; build the actual applicability matrix from the
stable standard during implementation.

## Release checklist
- [ ] Every TF ID maps to implementation and actual test/review evidence.
- [ ] All project/Inbox permissions apply to Dapper, search/counts, files and jobs.
- [ ] Refresh atomicity/replay/CSRF/logout/reset/MFA tests pass.
- [ ] Password/secret/token storage, encryption rotation and redaction reviewed.
- [ ] Completion retry/concurrency generates only one successor.
- [ ] Date-only views, month-end/leap/DST recurrence and reminders tested.
- [ ] Stale edit/reminder generations, revoked recipient access and retries tested.
- [ ] Attachment type/size/path/quarantine/scanner failure cases pass.
- [ ] Webhook/Push SSRF covers IPv6, redirects, rebinding and metadata.
- [ ] Queue crash recovery/fencing and external duplicate limitations documented.
- [ ] Keyboard/focus/contrast/screen-reader/themes/mobile checks completed.
- [ ] Actual load report states hardware/data/concurrency/p95 and failures.
- [ ] Compose fresh install, upgrade, backup/isolated restore verified.
- [ ] Secret/SAST/dependency/container scans and SBOM produced.
- [ ] License, docs, migration/release notes and security reporting contact finalized.

Evidence record fields: TF or control ID, code location, test/review command,
date/environment, result, reviewer, limitations. Missing checks remain unverified.

## References checked 2026-10-01
- .NET: https://dotnet.microsoft.com/en-us/platform/support/policy
- Requirements: https://www.iso.org/standard/72089.html
- Architecture: https://www.iso.org/standard/74393.html
- Security: https://github.com/OWASP/ASVS/releases
- Refresh/OAuth: https://www.rfc-editor.org/rfc/rfc9700.html
- JWT: https://www.rfc-editor.org/rfc/rfc7519
- JWT validation: https://www.rfc-editor.org/rfc/rfc8725
- Recurrence: https://www.rfc-editor.org/rfc/rfc5545
- Problem Details: https://www.rfc-editor.org/rfc/rfc9457
- Accessibility: https://www.w3.org/TR/WCAG22/

Recheck stable package/SDK versions at implementation; baseline .NET 10 LTS.

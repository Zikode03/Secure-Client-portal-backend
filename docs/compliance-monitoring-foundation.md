# Compliance monitoring foundation

Implements internal preparation for CIPC, SARS and CSD checks. **No authority connector, credentials, scheduled verification or provider approval is included.** This does not pass the live verification gate in compliance integration Phase 1.

## Database and deployment

Apply the `ComplianceMonitoringFoundation` EF migration before serving the matching frontend. It adds only:

- `AppComplianceMonitoringProfiles`: CSD supplier number and optimistic concurrency token.
- `AppComplianceCheckSettings`: per-business applicability and reason for each supported check.
- `AppComplianceVerifications`: append-only manual observations, source scope, dates, actor, evidence reference and identifier fingerprint.

Registration and tax numbers use the existing `AppClients` fields; there is no second copy. No new rows are seeded and reads do not write defaults. Deploy using the existing database backup/migration process; the migration was generated but not applied during implementation. Existing document compliance records are unaffected.

Example, run from the backend repository after checking the target database configuration:

```powershell
dotnet ef database update --project src/SecureClientPortal.Infrastructure --startup-project src/SecureClientPortal.Api
```

Do not run against an unidentified database. A rollback drops the new tables and their observations: back up first. Protect these records and the existing business identifiers using the deployment's database access, encryption and backup controls. Do not enter secrets in identifier/reason/evidence-reference fields.

## Contract

All endpoints require an authenticated `ClientOrAccountant` identity and the existing backend client scope. Staff must be administrators or assigned accountants to write. Clients can view only their allowed businesses.

| Method | Path | Purpose |
| --- | --- | --- |
| GET | `/api/compliance/monitoring/{clientId}` | Identifiers, configuration and latest manual observation per check; server-provided `canManage` |
| PUT | Same path | Save identifiers and complete check applicability list, using returned `version` |
| GET | `/api/compliance/monitoring/{clientId}/history?checkCode=...&page=1` | History, newest recorded first, 20 entries per page |
| POST | `/api/compliance/monitoring/{clientId}/manual-verifications` | Append a manual check; requires current version, applicable check, identifier, outcome, evidence reference, UTC check time and later review time |

Writes and redacted audit events save together. Stale versions return HTTP 409. There are no edit/delete-history endpoints and no endpoint that accepts an authority-verified method. Evidence references should point to records already held securely; they are not downloadable links or proof that the portal validated the evidence.

## Interpretation

- Applicability starts `undecided`, not required for every business. `not_applicable` requires a reason.
- Connection is always `not_connected` in this foundation, including after manual verification.
- No history means `not_checked`. Manual observations use `accountant_confirmed`, with a separate `pass`, `fail` or `unknown` outcome for that narrow check.
- After the staff-selected review time, the observation is `stale`. This follow-up date is not an authority validity guarantee.
- Changing the relevant business identifier makes the prior observation `identifiers_changed`. The original historical observation remains intact.
- Each CIPC scope is separate: registration, annual returns and beneficial ownership. Listing a scope does not confirm that an approved API will provide it.
- CSD supplier registration is not a blanket compliance/tender assessment. No whole-business compliance percentage is computed by these endpoints.
- Connection failures must not be converted into pass/fail. Future connectors need their own authorised server-side ingestion, status mapping, freshness policy and failure tests.

## Verification

`ComplianceMonitoringTests` covers scoped read/write/history access, no assumed checks, persistence, redacted audits, required reasons, validation, stale versions, append-only manual history, no fake connection and stale/changed-identifier results. Tests use an in-memory EF database; deployment must still exercise the migration against a backed-up SQL Server environment.

The backend can be built/tested with `--configuration ComplianceCheck` while Visual Studio owns Debug assemblies. This builds into a separate directory and does not stop or start the running API.

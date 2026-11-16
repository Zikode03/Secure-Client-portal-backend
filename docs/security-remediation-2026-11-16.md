# Security remediation — 16 November 2026

## Removed permissions stay removed

Explicit permission lists, including empty lists, are authoritative. Neither frontend helpers nor authenticated API authorization restore permissions from role defaults. API authentication reloads the current role permissions and replaces old permission/scope/membership claims before authorization. Current database assignments determine client access.

Sensitive client, document, request, role, user, assignment, report-export and settings operations now require their specific permission as well as existing role/client checks. Review paths through the review queue, invoice corrections and monthly slots are covered. Bulk import requires both client-create and document-upload permissions.

## Role edits invalidate sessions

Updating a role definition or disabling a role revokes sessions belonging to its users, including persistent sessions. Other roles’ sessions are preserved. Role edits and revocations save together. The existing session validator rejects revoked sessions; refresh cannot reactivate them.

A startup upgrade adds explicit operation permissions only to untouched builtin defaults with the expected scope. Administrator-edited roles are never expanded. Affected users must sign in again. Administrators using custom restricted definitions must explicitly grant any newly introduced operation they intend to allow.

## Dependencies and regression prevention

The backend pins System.Security.Cryptography.Pkcs 8.0.2. Frontend upgrades include Vite 8.3.8, React plugin 6.3.0, Vitest 4.1.12 and compatible CSS tooling. Both advisory audits report zero current vulnerabilities. This is a point-in-time advisory result, not a guarantee against undiscovered vulnerabilities.

CI now checks dependency advisories alongside builds, contracts and tests. Frontend installation no longer requires legacy peer-dependency resolution.

## Validation

316 backend tests and 382 frontend tests passed locally. Negative tests cover denied read-only operations, empty permissions, no administrator wildcard, current-claim replacement, role edit/disable revocation, unrelated sessions, alternate review entry points, direct client-service denial and preservation of edited builtin roles. The updated isolated security reproductions also pass. Build and OpenAPI contract checks pass.

The other review items, including complete backup/restore verification and deployment-specific security validation, are not closed by these changes.

# System audit — 10 November 2026

## Resolution after the requested fixes

The findings below preserve the audit baseline. All six user-requested groups
have now been addressed:

1. Private portal state is isolated by user, role and client scope. Live domain
   records are no longer persisted in browser storage; demo state and drafts use
   scoped session storage. Identity changes/logout clear private state, and
   cross-tab account/logout changes invalidate the local workspace.
2. Failed logout reports an unconfirmed server sign-out, clears the local
   workspace and sets a pending marker that blocks automatic cookie restoration.
   The login page provides retry. Expired access sessions can refresh to revoke
   the remaining server session instead of silently leaving it active.
3. Full test suites now pass: **260 frontend / 231 backend / zero failures**.
   Recurring default dates follow the open reporting period; fixtures also use
   an explicit clock. Compliance tests open the intended tab and assert the
   relevant error, preserving upload-failure and readiness checks.
4. All **186 operations** have response contracts or explicit empty responses.
   A source analyzer derives the previously missing schemas and CI checks them.
   Monthly packs, document uploads and review mutations now use generated clients.
   Migration caught and fixed the real `canSubmit`/`canCurrentlyBeSubmitted` mismatch.
5. Manual registration uses a stable GUID, supports distinct accounts sharing
   final digits, and rejects reused IDs with conflicting details. SQL writes and
   audit entries now share a transaction. **Nine real SQL checks passed**, covering
   correct audit references, idempotency and rollback; the isolated DB was removed.
6. Preferences load/save through the existing authenticated account API. Reminder
   and escalation delivery respects saved flags and South Africa quiet hours;
   security events remain enabled. Setup examples and defaults now use API port
   **5127** and project SQL port **14330** consistently.

The frontend build and contract drift checks pass. The running API still needs a
rebuild/restart to load the changes. The original full-browser/deployment checks
listed at the end remain separate release verification work.

This is a source review and test audit of both current local repositories. It is
not a completed browser, production-infrastructure or penetration test. No bank
integration was enabled and no database migration was run during this review.

## Highest-priority findings

### 1. Browser state is not consistently isolated between signed-in users

`src/app/portal.tsx:510` defines one client-portal localStorage key. Reading and
writing that state does not include a user/client identifier. The persisted state
includes documents, invoices, requests, client settings and activity. The
AuthProvider logout path clears its frontend-session key, not this portal cache.
`src/components/inbox/useInboxDraft.ts:4` similarly keys drafts by audience/thread,
without the signed-in user. Two staff members can share access to one thread.

The confirmed finding is unsafe cache scoping/retention. Actual cross-user display
or disclosure has not been reproduced in a browser. Backend authorization was not
shown to be bypassed. Scope sensitive state to the authenticated user/client,
clear it on identity changes/logout, and test two users on the same browser.

### 2. Logout can appear complete when server-session invalidation failed

`src/app/auth.tsx:671` catches and ignores backend logout errors and then clears
local UI state. HttpOnly cookies cannot be cleared by this client code. A failed
server logout therefore does not prove the session or refresh token was revoked.
Test this under network failure and show an accurate outcome/retry path.

### 3. Complete test suites are not green

Fresh backend execution: **225 passed, 4 failed, 229 total**. Failures concern
recurring due dates, recurring-item inheritance, template inheritance and
operating-profile requirements. The same four failures were previously reproduced
against an unchanged backend HEAD snapshot. These failures must be diagnosed;
they are not proof by themselves that the corresponding live workflows fail.

Fresh frontend execution: **243 passed, 10 failed, 253 total**, with failures in
compliance-centre, compliance-lifecycle and shared-firm-page assertions. These ten
were already documented before the recent changes.

The full frontend run also caught a new setup regression: Vitest discovered
`tools/generate-api.test.mjs`, which uses Node's test runner. This audit corrected
that collision by renaming it to `tools/check-api-generation.mjs` and updating
`api:test`. The Node test passes and Vitest's file listing now excludes it. The
full frontend suite was not rerun after this filename-only correction.

## Functional and contract gaps

### 4. OpenAPI adoption is incomplete

The export has **145 paths / 186 operations**. A count of exported operations is
not a count of complete typed contracts: **145 operations have no documented
success-payload schema** in the current snapshot. Some may legitimately return no
body and should have that behavior documented accurately. Only the Banking
service wrappers have migrated to the generated client.

Monthly packs, document uploads/reviews and compliance need actual response
metadata and gradual client migration. Frontend CI checks its committed snapshot;
it does not automatically compare an unrelated backend branch or deployed API.
Use `api:sync --check` against the intended backend checkout for that comparison.

### 5. Manual account identity can collide

Backend `BankingService.Manual.cs:28` derives an account key from client, bank name
and last four digits. Two distinct accounts at the same bank with matching final
digits get the same key. Registration returns the existing account instead of
identifying this ambiguity. A display label is not a reliable account identity.
Use a distinct stable manual account ID plus explicit duplicate/ambiguity handling.
This gap was introduced in the recent manual-statement work.

### 6. Import persistence and its audit entry are not atomic

Backend `BankingService.Manual.cs:105` commits imported transactions before the
PortalDbContext audit write at line 111. An audit-write failure can produce a
failed request after data was saved. Deduplication protects repeat imports but
does not guarantee the missing audit entry is recovered. Use an atomic shared
transaction or a durable audit/outbox process, and test failures on SQL Server.
This limitation was documented but remains unresolved in the recent implementation.

### 7. Staff notification preferences are browser-local

`src/pages/shared/NotificationPreferencesPage.tsx:141` saves staff preference
settings into localStorage and reports success without an API call. Changing
these settings does not update a server-side reminder schedule or carry to another
device. Persist them through a backend endpoint or clearly label the current scope.

### 8. Setup documentation still disagrees with the running configuration

The actual frontend proxy targets port **5127**, matching the backend launch
profile. `.env.example` still points to **4000**, and Vite's default proxy fallback
also uses 4000. The backend README still describes SQL port **1433** although the
project container and corrected development settings use **14330**. A fresh setup
can reproduce connection failures. Unify the defaults and add a startup diagnostic.

## Still unverified

- A complete real-browser journey: sign-in, assigned-client selection, upload,
  accountant review, rejection/re-upload, monthly-pack submission and closure.
- Cross-user cache isolation, session expiry and logout during API/network failure.
- Actual invitation/reset-email delivery, production file scanning and file recovery.
- SQL-backed concurrent import behavior and recovery after an audit-write failure.
- Production deployment configuration, backups and a successful restore exercise.

SQL connectivity to `secure_client_portal_dev` was independently verified after
the port correction. That verification did not prove API startup, migrations,
every endpoint, email delivery, storage or the whole user workflow.

Prioritize identity/session isolation and failing core-workflow tests, then finish
the core OpenAPI contracts and setup consistency. Keep external bank integration
paused throughout.

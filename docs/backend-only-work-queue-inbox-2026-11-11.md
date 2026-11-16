# Backend-only portal, Work Queue and Inbox fixes - 11 November 2026

## Work Queue

Removed the local-record fallback and 'Reload demo queue' action. The queue now begins empty while requesting authenticated API data, validates returned rows and workspace shape, reports failures and retries the API. A read-only diagnostic against the configured SQL database successfully executed the actual ReviewQueueService query for existing admin and accountant scopes, each returning one pending document.

Fixed a preview race: updating workspace metadata previously invalidated the effect that was still downloading the file. A stable cache now keeps that download alive until selection/visibility changes; object URLs are reclaimed on replacement/unmount. A regression holds file bytes pending until after metadata renders, then checks that the actual PDF iframe appears.

## Inbox

The index previously fetched every conversation workspace before displaying any requests and repeated that batch every 20 seconds. It now maps the request index immediately, fetches only the selected conversation, caches it against the server update timestamp and supplies conversation refresh. Overlapping index polls are suppressed, background-tab polls are skipped and stale responses are ignored. Generation-aware request guards also handle React Strict Mode's mount/cleanup cycle without delaying initial loading until the next poll.

A regression renders 30 request records in Strict Mode while conversation details are intentionally delayed. It proves the index is visible and only one selected conversation request is made. This is an automated request-count/state test, not a measured promise of production load time.

## No application demo data

- Authentication always uses the backend. Built-in demo users/passwords and browser-stored session restoration as an authentication source are removed.
- Business, document, compliance, staff and portfolio loading states are empty. Sample clients, sessions, reports, signatures, filenames/previews and reset actions are removed from shipped code.
- The frontend build rejects historical demo identities/credentials if they reappear in output chunks.
- Backend startup no longer creates development users, businesses, packs or slots. Configuration templates and default role definitions remain because they are application configuration, not customer records.
- Simulated email/log delivery cannot claim an invitation was sent. SMTP must be configured; failures are reported. Existing development SMTP configuration is preserved, but no email was sent as part of these checks.

## Test boundaries

Backend mode is the default for tests. Shared accountant pages, document menus, notifications, profile, Work Queue and Inbox tests use backend-contract response fixtures. Old pure state/client/layout unit tests explicitly opt into isolated fixtures under src/test/fixtures; those files are not imported by the application and do not enter shipped bundles. Their unit assertions are not represented as live API checks.

Final verification: 306 frontend tests, 238 backend tests, TypeScript/build, 186-operation OpenAPI consistency and a read-only real SQL Work Queue diagnostic passed. A full authenticated browser walkthrough remains outstanding following the earlier browser automation safety stop.

## Existing database and real accounts

Read-only inspection found three existing development login accounts, one Acme Holdings business and one uploaded document. These existing rows were preserved to avoid removing administrator access or losing the upload. This task does not claim the database was emptied. The user directed the next stage toward creating real email accounts through Users & Access. Once a real administrator is active and the existing business/document has an agreed owner, the old development identities can be explicitly retired.

Rebuild/restart the API and restart the frontend to activate the latest source. Use Users & Access to create accounts with real email addresses. No recipient was supplied and no account/invitation email was created or sent by this task.

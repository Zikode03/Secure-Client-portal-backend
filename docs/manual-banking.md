# Manual banking operation

Direct bank integration is paused. Review the decision on **10 November 2027**.
There is no scheduled automatic reactivation.

## Client and accountant workflow

1. Open **Bank Statements** and register every active ZAR business account using
   a bank name, account label and last four digits. No banking credentials are requested.
2. Open the reporting month in **Monthly Packs** and upload a statement into each
   account's required slot. Until accounts are registered, a general statement slot
   remains required. Named account slots replace the empty general placeholder;
   previously uploaded evidence remains visible.
3. Optionally import the bank's CSV export in Bank Statements. Choose the account,
   map columns and select explicit date and decimal formats. Review the server
   preview and confirm to save. Import at most 5,000 rows / 4 MB at once.
4. Accountants can open **Review bank statements and imported transactions** in
   the client pack workspace. Review the statement, transactions, invoices and
   receipts through the existing document review process before closing the pack.

PDFs remain statement evidence; they are not automatically extracted into
transactions. CSV imports are unreviewed user-supplied data. They never certify
provider coverage or automatically approve documents or complete a pack.
The transaction list displays the latest 50 records, including retained history.

## CSV rules

CSV requires unique headers and consistent columns. Comma and semicolon delimiters,
quoted fields and UTF-8 BOM are supported. Date format is selected explicitly:
YYYY-MM-DD, DD/MM/YYYY or MM/DD/YYYY. Amounts use either a signed column
(negative debit / positive credit) or separate positive debit and credit columns.
Choose point or comma decimals; grouped thousands must use the opposite separator.
Reference and running balance are optional. Invalid dates, zero amounts, excess
precision and malformed rows reject the entire batch.

Duplicate detection uses the account, date, trimmed description/reference, signed
amount and balance. Occurrence numbers preserve repeated identical rows within
one statement. Re-importing the same rows skips existing occurrences. Banks that
omit balance/reference can have indistinguishable legitimate transactions across
different files; the preview labels duplicates as likely duplicates. Check overlaps
against source statements. No browser storage is used for transaction persistence.

## Backend implementation and release

The matching backend lives at
`C:\Users\ngceb\OneDrive\Desktop\Client-portal-backend\Secure-Client-portal-backend`.
Release frontend and backend together. Restart/rebuild the running API to load its
new endpoints; source changes do not update an already-running Visual Studio process.

`BankingOptions.IntegrationEnabled` defaults to `false`. Keep
`Banking__IntegrationEnabled=false` in hosting configuration. Sandbox availability
alone cannot enable connections. Provider sync calls are rejected before invoking
a provider. No separate bank-sync background job was found; normal monthly-pack
automation remains enabled. Existing consent, connections and transaction history
are preserved. Manual accounts use a disconnected `manual_csv` connection record
for storage and never grant consent or count as a live feed.

Existing Banking tables are reused; this change adds no database migration.
Manual imports use server-side client authorization, validation and database
duplicate checks. SQL imports run in a serializable transaction with the existing
unique transaction index. The Banking and Portal contexts share one SQL connection
and transaction: account/statement changes, imported rows and their audit entries
commit together. An audit failure rolls the operation back. Registration uses a
client-supplied stable GUID for idempotency, rather than assuming bank name and
last four digits uniquely identify an account. Distinct accounts can share those
digits; reusing a registration ID with different details is rejected.

Editable packs restore required statement evidence even when old live connections
exist. Under-review, completed and closed packs retain their historical evidence.
Existing document submission, rejection, approval and closure rules remain in force.
Account registration does not discover unregistered bank accounts automatically.

Future reactivation requires a deliberate configuration change and a coordinated
frontend update restoring provider controls, with approved bank access and fresh
coverage/consent checks. Do not treat the review date as authorization to reactivate.

## Verification

Updated after the system-audit fixes: all 260 frontend tests and 231 backend tests
pass. Nine isolated SQL Server checks verify registration identity, idempotency,
correct audit references, rollback on audit failure and import deduplication.
The temporary verification database was removed. The historical results below
record the earlier implementation baseline and its limitations at that time.

- Frontend TypeScript and local-check bundle passed.
- Focused banking, CSV and navigation checks: 45 passed.
- Backend banking regression checks: 32 passed, including multi-account closure.
- Broader backend run: four recurring/profile failures also reproduced in a clean
  HEAD snapshot. They are unrelated to this change.
- Broader frontend run found ten existing compliance/shared-page failures plus
  the old Banking navigation assertion. The assertion was updated for Bank
  Statements and the navigation suite passes. Existing failures are also recorded
  in `banking-monthly-pack-review.md`.
- No live database migration, live API restart, SQL-backed import concurrency test
  or browser end-to-end check was performed. Unit tests use in-memory databases;
  the local-check bundle is not a production deployment.

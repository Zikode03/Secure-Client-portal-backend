# Phase 3 — Account security

## Implemented

- Every new/reset/changed password requires 15 Unicode characters (max 1024 UTF-16
  code units). The minimum remains 15 even after MFA is active. Passwords are not
  trimmed before hashing. Existing short-password accounts without MFA must use
  Forgot password; existing passwords are never silently changed by deployment.
- Password creation calls the HIBP range API with only five SHA-1 prefix characters
  and padded responses. SHA-1 is used only for this lookup, never password storage.
  A positive match, malformed response or outage prevents accepting the password.
- Account lockout persists in SQL: five failed password/MFA attempts, 15 minutes.
  SQL Server application locks serialize account guesses across API instances;
  version concurrency and consumed-token checks prevent lost state/token reuse.
  Existing per-IP limits still apply. Successful full authentication clears failures.
- Admin/accountant password verification yields only a five-minute MFA challenge,
  never an access/refresh cookie. First-time enrollment uses an authenticator setup
  key. TOTP uses 30-second steps, a one-step time allowance and replay protection.
  Keep API clocks synchronized. Sessions record successful MFA; pre-upgrade staff
  sessions cannot pass authorization or refresh.
- Ten random 128-bit recovery codes are displayed once and stored only as hashes.
  Recovery requires the password challenge plus an unused recovery code, revokes
  sessions and requires a replacement authenticator before access is granted.
  Password reset emails never remove MFA. No email-only MFA bypass exists.
- Invites expire after 24 hours; both administrator and self-service password reset
  links expire after 30 minutes. Links are single-use and supersede earlier setup
  links. Admin responses no longer return a temporary password or setup URL.
- Self-service reset requests return the same response for known/unknown accounts,
  do not disable active accounts and are limited to once per account per 5 minutes.
- Admin Settings > Production email delivery sends a code to the signed-in admin.
  Confirming that code within ten minutes records receipt and an audit event.
  Receipt proof is bound to the current mail configuration and recipient; changing
  either invalidates the displayed verification.
  Merely accepting an SMTP send is not treated as verified inbox delivery.

## Deployment and recovery gate

Deploy frontend/backend together. Apply migration
`20260910185255_Phase34AccountSecurity` before serving traffic. Rebuild/restart
the API in Visual Studio yourself for local testing; no API was started by this work.
The current startup migration mechanism remains until the release phase changes it.

Allow backend HTTPS access to `api.pwnedpasswords.com:443`. Use the Phase 1 SMTP
configuration; store SMTP credentials outside source. Run the receipt test using
the actual production SMTP account and an actual admin inbox after deployment,
and repeat whenever mail configuration changes. Also test an invite and password
reset in staging; spam placement and real delivery cannot be established by mocks.

Existing local seeded passwords are short: request an email password reset before
initial MFA enrollment. Development mail logging no longer prints token URLs.
If SMTP is unavailable, configure it before onboarding; no temporary credential
fallback was introduced.

If both authenticator and recovery codes are lost, escalate for a separately
verified, audited operator recovery. The application deliberately has no
administrator button that bypasses a colleague's MFA. Protect backups of the
database, key ring and certificate: MFA secrets also use the key ring.

Gate remains open until every live admin/accountant has enrolled, saved recovery
codes and demonstrated authenticator/recovery login and real email delivery.

References: [HIBP range API](https://haveibeenpwned.com/API/v3#SearchingPwnedPasswordsByRange),
[RFC 6238](https://www.rfc-editor.org/rfc/rfc6238).

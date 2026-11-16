# Local application security assessment — 16 November 2026

## Scope and method

49 active HTTP checks against the actual Release API process, hosted on a temporary loopback port in Development with a new randomly named SQL Server database and private temporary document/key directories. The harness never reads the application's database connection settings. It creates only isolated synthetic users, two businesses, an accountant assignment, monthly packs and encrypted PDF fixtures. Those records are test fixtures, not application demo data. Email delivery and external integrations are disabled. The API process, test database and private storage are removed after the run.

Positive controls establish that own-client requests, assigned-accountant requests, real client password/cookie login, CSRF-authorised profile changes and encrypted document downloads work. Negative cases then change one access condition at a time. Privileged and inconsistent JWTs use an assessment-only randomly generated signing key configured only on the isolated process. This is a white-box test technique, not evidence that an attacker can sign tokens.

## Finding and correction

The initial run passed 28 of 29 checks. The API matched the JWT session identifier but did not explicitly bind that session's UserId to the token subject. A test token signed with the assessment key, identifying client A but using client B's session identifier, received HTTP 200. This is a session-validation hardening gap: ordinary attackers still cannot modify a token without invalidating its signature.

Authentication now rejects a session whose UserId differs from the resolved current user. The same active regression returns HTTP 401. Unknown, revoked, expired and non-MFA staff sessions are also rejected.

## Final results

All **49 HTTP checks passed**. Coverage includes:

- Anonymous access; client-to-client profile, document metadata, comment and download isolation in both directions.
- Assigned and unassigned accountants; membership and assignment removal during existing sessions.
- Forged signatures, wrong audience, expired tokens, unknown/mismatched sessions, disabled accounts and revoked sessions.
- Removal of privileged token claims in favour of current database permissions; client approval denial; direct restricted-role create denial.
- Real client password login, HttpOnly/SameSite=Strict cookies, cookie authentication, CSRF logout and rejection after logout.
- Staff MFA requirement, fabricated MFA challenges, fabricated invite/reset setup tokens and a SQL-like email login attempt.
- Missing CSRF, hostile Origin with valid CSRF, accepted authorised write, repeated-login throttling and no-store CSRF responses.
- Successful encrypted-document decryption/download, denied cross-client upload and rejection of executable contents/extensions.

The complete existing backend suite also passed **327 tests**, including reset-token replay, recovery, file upload policies, permissions and transport tests. The frontend request-security suite passed **10 tests**.

## Reproduction and CI

Build the API in Release, then run `dotnet run --project tools/SecurityProbe/SecurityProbe.csproj -c Release -- .` from the backend root. By default it uses local SQL Server Windows integrated authentication. For a disposable CI SQL server, supply a connection to master through `PORTAL_SECURITY_TEST_SQL`; never use command-line credentials. The harness always generates its own database name and creates/drops only that database. It requires database-create permission. The JSON result is written to `TestResults/security-http-assessment.json` without passwords, token values or document contents. CI runs this check against its disposable SQL container and uploads that report.

## Limits

This is an automated, authenticated, source-assisted local assessment, not independent penetration-test certification. It does not establish production TLS, reverse-proxy, firewall, storage ACL, live ClamAV or SMTP security. Development uses the baseline scanner and permits HTTP cookies; production configuration and existing production-security tests enforce stronger requirements. No live customer data, real emails, external targets, malware or destructive load were used. Broader browser/XSS, business-logic abuse, concurrent-session races and infrastructure testing remain separate assessment work. Successful sampled SQL-like input is not a proof that all queries resist injection.

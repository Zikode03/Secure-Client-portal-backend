# Administration — single-firm release

This installation serves one accounting firm. Super-admin accounts and shared multi-firm tenancy are deferred; no multi-firm isolation or platform-administration functionality is claimed.

## Administrative authority

The builtin administrator retains firm-wide oversight and user, role, client, assignment, import and settings management. It no longer inherits accountant/client workspace access or document-review authority. An administrator who must also perform accounting review needs an explicit documents.review grant. Closing a month requires that review permission at the API and service boundary. Existing untouched broad builtin admin definitions are narrowed at startup and their sessions revoked; edited restricted role definitions remain unchanged. Users affected by this policy upgrade must sign in again. The last active administrator cannot be disabled or demoted, and the builtin admin role cannot be disabled or lose its administrator workspace scope.

## Access administration

Invited users have a dedicated Resend invitation action. It rotates setup tokens, limits rapid resend attempts, preserves account status/password and reports delivery outcomes. Active users are directed to password reset rather than invitation resend. Sign out sessions revokes current sessions and refresh credentials without disabling the account or changing its password. Access, settings, templates and operational endpoints enforce their specific permissions. Role editing now reads the actual API permissions array and saves backend operation keys instead of incompatible frontend permission aliases; unlisted existing keys are preserved.

## Settings and branding

Firm name and an optional PNG/JPEG logo are displayed in staff and client workspaces. Logos are limited to 100 KB and checked by MIME prefix and file signature. Remote image URLs and SVG are rejected. The workspace identity endpoint exposes only name/logo, not firm contact, registration or tax details.

Saved reminder wording is used by monthly-pack and compliance reminders, with {item} and {dueDate} placeholders. Existing template/rule IDs update in place without EF tracking collisions. Startup does not reset edited settings or recreate deliberately removed rules. Deleting a required-document template still used in a pack template is rejected with guidance. Settings audits record the changed key, not its potentially sensitive value.

## Live operational oversight

The admin dashboard shows outstanding/overdue required documents, overdue requests, months under review/completed, notification email backlog/failures, actual enabled SMTP delivery configuration, storage usage/limit and automation run results. Automation records running/succeeded/failed results. The recovery CLI records verified backup/restore outcomes after its checks; the generic settings API cannot overwrite these operational records.

Unknown recovery results remain unknown; previous backups are not fabricated or backfilled. Backups still require a maintenance window with all writers stopped. Scheduling, secure secret custody and offsite retention are hosting operations, not actions triggered from a running portal. Storage results indicate availability and measured usage, not proof of filesystem ACLs or offsite durability. SMTP enabled configuration is not proof of recipient receipt; the existing email verification screen remains the receipt check.

## Validation

- 340 backend tests passed locally, including continuity, permission defaults/upgrades, invitation rotation, session revocation, audit privacy, logo validation, real operational counts, configuration persistence and same-ID saves.
- 390 frontend tests passed locally, including actual role API arrays, invitation delivery feedback, unavailable operational data and workspace routing.
- 67 real HTTP checks passed against a disposable SQL database and private temporary storage. New cases cover admin review/close denial, each settings/template save, read-only admin mutations, operational status protection, last-admin protection and private branding.
- Backend build, 201-operation OpenAPI metadata/contract checks and frontend build pass. CI includes the active HTTP checks and full SQL/document recovery rehearsal.

Test records are isolated fixtures, never application demo records. No live client records or actual email recipients were used.

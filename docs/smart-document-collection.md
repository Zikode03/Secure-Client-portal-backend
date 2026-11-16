# Smart document collection - 11 November 2026

Implemented within the existing monthly-pack workflow. Direct bank integration remains paused and the separate Bank Statements module remains hidden.

## Phone scanner

Invoice and receipt uploads offer a camera input, local image preview and optional on-device English OCR for JPG/PNG photos. Tesseract worker, WASM and English language assets are served from the portal origin in development and build output. No external OCR service or CDN receives document images. Only the normal confirmed upload sends the document to the portal backend.

Supplier, invoice/receipt number, date and total are editable suggestions, not verified accounting data. ZAR amounts are supported in this first version. Confirming supplier/date/amount is required when saving extracted or manually entered invoice details. Editing a field invalidates confirmation. Fields can be left entirely blank for an ordinary upload. PDF/Office uploads remain available without automatic extraction. Users can cancel scanning; failed scans permit manual entry.

Generic camera filenames are accepted. The existing naming suggestion now incorporates confirmed supplier, date and document number. Backend document/version display names use the suggestion; document versions preserve the original filename separately. The additive `20261111075508_InvoiceScanDetails` migration stores the confirmed details in one nullable document column.

## Possible duplicates

Client-side warnings explain matching fields. The backend checks again, within the same client, before storing the file. Two or more matching nonempty supplier, invoice number, date or amount fields trigger a warning. A matching amount alone is insufficient. Confirmed separate documents can be saved by explicitly acknowledging the warning. The acknowledgement is recorded in upload audit metadata. This is advisory detection, not a guarantee that every duplicate will be found. Older records without confirmed metadata cannot participate in metadata matching. Original filenames alone do not establish identity.

## Requirement reminders

The existing automation worker now creates in-portal reminders for required missing documents within seven days of their due date, overdue documents, and rejected/reupload-requested documents. It sends at most one reminder per recipient, requirement and reminder type within a rolling 24-hour window. Existing notification preferences and quiet hours apply. Business-date comparisons use South Africa time.

Uploaded drafts, submitted, under-review, accepted, not-applicable, completed and closed requirements are excluded from requirement chasing. General pack-deadline reminders also stop when all required documents have been submitted or otherwise satisfied. Notifications already sent remain history. This work does not introduce SMS/WhatsApp or promise email delivery.

## Monthly checklist

The existing client-specific monthly checklist remains the source of requirements, with separate states, attention filtering and individual upload/submission actions. An explicit explanation now clarifies that missing items do not block uploading/submitting other available documents. Invoice slots accept JPG/PNG photos. Requirement selection remains controlled by the client's approved profile/template rather than forcing every document type on every business.

## Verification and activation

Full frontend suite: 267 passed. Full backend suite: 235 passed. Actual OCR on a generated receipt image recognized supplier, invoice number, date and total. Server upload tests cover confirmation, duplicate warning/override and original filename retention. Reminder tests cover repeat suppression and stopping after upload. OpenAPI source contracts and generated types match all 186 operations.

The API running in Visual Studio has not been restarted, and the project database migration has not been applied in this task. Rebuild/restart the backend with its normal development migration process before using the new upload fields, then restart the frontend so its new dependency/plugin configuration is loaded. A complete live phone/browser-to-database walkthrough is still outstanding.

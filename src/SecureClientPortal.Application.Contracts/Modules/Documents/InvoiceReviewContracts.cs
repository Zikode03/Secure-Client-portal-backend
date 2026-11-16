namespace SecureClientPortal.Backend.Application.Contracts.Modules.Documents;

public sealed record InvoiceReviewValues(
    string SupplierName, string InvoiceNumber, string InvoiceDate, string Currency,
    string InvoiceTotal, string AmountDue, string Subtotal, string VatAmount,
    string Adjustments, bool AllAdjustmentsAccountedFor);
public sealed record InvoiceReviewEvidence(string Field, string Text, double X, double Y, double Width, double Height, double? Confidence, int PageIndex = 0);
public sealed record InvoiceReviewLineItem(string Description, string Quantity, string UnitPrice, string Discount, string NetTotal);
public sealed record SaveInvoiceReviewRequest(Guid RequestId, int ExpectedVersionNumber, int ExpectedRevision,
    InvoiceReviewValues Values, InvoiceReviewEvidence[] Evidence, bool OriginalConfirmed,
    string? ReviewNote, string Source, InvoiceReviewValues? Suggestions = null, InvoiceReviewLineItem[]? LineItems = null, bool LineItemsConfirmed = false);
public sealed record InvoiceReviewContext(int ExpectedVersionNumber, int ExpectedRevision);
public sealed record InvoiceReviewFinding(string Id, string Rule, string Result, string Message,
    string[] Fields, Guid? RelatedDocumentId = null, string? RelatedDocumentName = null);
public sealed record InvoiceReviewHistory(Guid Id, int VersionNumber, int Revision, Guid ActorUserId,
    DateTime CreatedAtUtc, string Source, string? ReviewNote, InvoiceReviewValues Values, InvoiceReviewLineItem[]? LineItems = null, bool LineItemsConfirmed = false);
public sealed record InvoiceReviewResponse(Guid DocumentId, int VersionNumber, int Revision, string State,
    bool IsStale, bool CanAccept, int Year, int Month, string RuleSetVersion,
    InvoiceReviewValues Values, InvoiceReviewEvidence[] Evidence, bool OriginalConfirmed,
    string? ReviewNote, InvoiceReviewFinding[] Findings, InvoiceReviewHistory[] History, string? LegacyAmount = null, InvoiceReviewValues? Suggestions = null, InvoiceReviewLineItem[]? LineItems = null, bool LineItemsConfirmed = false);

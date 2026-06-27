using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Contracts;

namespace SecureClientPortal.Backend.Application.Documents;

public static class DocumentValidators
{
    public static void ValidateUpload(UploadDocumentRequest request)
    {
        var errors = new List<string>();
        if (request.ClientId == Guid.Empty) errors.Add("Client id is required.");
        if (string.IsNullOrWhiteSpace(request.DocumentType)) errors.Add("Document type is required.");
        if (request.File is null || request.File.Length == 0) errors.Add("A file is required.");
        if (!request.MonthlyPackId.HasValue && !request.DocumentSlotId.HasValue)
        {
            errors.Add("A monthly pack or document slot is required.");
        }
        ThrowIfAny(errors);
    }

    public static void ValidateCreate(CreateDocumentRequest request)
    {
        var errors = new List<string>();
        if (request.ClientId == Guid.Empty) errors.Add("Client id is required.");
        if (request.MonthlyPackId == Guid.Empty) errors.Add("Monthly pack id is required.");
        if (string.IsNullOrWhiteSpace(request.Name)) errors.Add("Document name is required.");
        if (string.IsNullOrWhiteSpace(request.Category)) errors.Add("Document category is required.");
        if (string.IsNullOrWhiteSpace(request.FileType)) errors.Add("File type is required.");
        if (request.SizeBytes < 0) errors.Add("Document size cannot be negative.");
        if (request.UploadedByUserId == Guid.Empty) errors.Add("Uploaded by user id is required.");
        ThrowIfAny(errors);
    }

    public static void ValidateUpdate(UpdateDocumentRequest request)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(request.Name)) errors.Add("Document name is required.");
        if (string.IsNullOrWhiteSpace(request.Category)) errors.Add("Document category is required.");
        if (string.IsNullOrWhiteSpace(request.Status)) errors.Add("Document status is required.");
        if (request.SizeBytes < 0) errors.Add("Document size cannot be negative.");
        ThrowIfAny(errors);
    }

    public static void ValidateReview(AddReviewDecisionRequest request)
    {
        var decision = request.Decision?.Trim().ToLowerInvariant() ?? string.Empty;
        if (decision is not ("under_review" or "accepted" or "rejected"))
        {
            throw new AppValidationException("Decision must be under_review, accepted, or rejected.");
        }
    }

    public static void ValidateReupload(RequestReuploadRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new AppValidationException("A reason is required when requesting a re-upload.");
        }
    }

    public static void ValidateComment(AddDocumentCommentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            throw new AppValidationException("Comment message is required.");
        }
    }

    private static void ThrowIfAny(List<string> errors)
    {
        if (errors.Count > 0)
        {
            throw new AppValidationException(errors.ToArray());
        }
    }
}

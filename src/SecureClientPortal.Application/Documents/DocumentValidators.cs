using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Contracts;

namespace SecureClientPortal.Backend.Application.Documents;

public static class DocumentValidators
{
    public static void ValidateUpload(UploadDocumentRequest request)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(request.ClientId)) errors.Add("Client id is required.");
        if (string.IsNullOrWhiteSpace(request.DocumentType)) errors.Add("Document type is required.");
        if (request.File is null || request.File.Length == 0) errors.Add("A file is required.");
        if (string.IsNullOrWhiteSpace(request.MonthlyPackId) && string.IsNullOrWhiteSpace(request.DocumentSlotId))
        {
            errors.Add("A monthly pack or document slot is required.");
        }
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

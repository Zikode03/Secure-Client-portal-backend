using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Contracts.Modules.Requests;
using SecureClientPortal.Backend.Domain.Shared.Modules.Requests;

namespace SecureClientPortal.Backend.Application.Modules.Requests;

public static class RequestValidators
{
    public static void ValidateCreate(CreateRequestRequest request)
    {
        if (request.ClientId == Guid.Empty) throw new AppValidationException("Client is required.");
        if (string.IsNullOrWhiteSpace(request.Title)) throw new AppValidationException("Title is required.");
        if (string.IsNullOrWhiteSpace(request.Description)) throw new AppValidationException("Description is required.");
        _ = RequestDomainValues.ToRequestPriority(request.Priority);
    }

    public static void ValidateUpdate(UpdateRequestRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title)) throw new AppValidationException("Title is required.");
        if (string.IsNullOrWhiteSpace(request.Description)) throw new AppValidationException("Description is required.");
        _ = RequestDomainValues.ToRequestPriority(request.Priority);
    }

    public static void ValidateStatusUpdate(UpdateRequestStatusRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Status)) throw new AppValidationException("Status is required.");
    }

    public static void ValidateComment(AddRequestCommentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Message)) throw new AppValidationException("Message is required.");
    }

    public static void ValidateResolve(ResolveRequestRequest request)
    {
    }

    public static void ValidateEscalation(EscalateRequestRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.EscalateToRole))
        {
            var role = request.EscalateToRole.Trim().ToLowerInvariant();
            if (role is not ("admin" or "accountant"))
            {
                throw new AppValidationException("Escalation role must be admin or accountant.");
            }
        }
    }

    public static void ValidateUpload(UploadRequestDocumentRequest request)
    {
        if (request.File is null) throw new AppValidationException("File is required.");
        if (request.File.Length <= 0) throw new AppValidationException("File is required.");
    }
}

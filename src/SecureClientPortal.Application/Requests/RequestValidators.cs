using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Contracts;

namespace SecureClientPortal.Backend.Application.Requests;

public static class RequestValidators
{
    public static void ValidateCreate(CreateRequestRequest request)
    {
        var errors = new List<string>();
        if (request.ClientId == Guid.Empty) errors.Add("Client id is required.");
        if (string.IsNullOrWhiteSpace(request.RequestType)) errors.Add("Request type is required.");
        if (string.IsNullOrWhiteSpace(request.Title)) errors.Add("Request title is required.");
        if (string.IsNullOrWhiteSpace(request.Description)) errors.Add("Request description is required.");
        if (!IsAllowedPriority(request.Priority)) errors.Add("Priority must be low, medium, high, or urgent.");
        ThrowIfAny(errors);
    }

    public static void ValidateUpdate(UpdateRequestRequest request)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(request.RequestType)) errors.Add("Request type is required.");
        if (string.IsNullOrWhiteSpace(request.Title)) errors.Add("Request title is required.");
        if (string.IsNullOrWhiteSpace(request.Description)) errors.Add("Request description is required.");
        if (!IsAllowedPriority(request.Priority)) errors.Add("Priority must be low, medium, high, or urgent.");
        if (string.IsNullOrWhiteSpace(request.Status)) errors.Add("Status is required.");
        ThrowIfAny(errors);
    }

    public static void ValidateStatusUpdate(UpdateRequestStatusRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Status))
        {
            throw new AppValidationException("Status is required.");
        }
    }

    public static void ValidateComment(AddRequestCommentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            throw new AppValidationException("Comment message is required.");
        }
    }

    public static void ValidateResolve(ResolveRequestRequest request)
    {
        if (request.ResolutionNote is { Length: > 1000 })
        {
            throw new AppValidationException("Resolution note is too long.");
        }
    }

    private static bool IsAllowedPriority(string value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized is "low" or "medium" or "high" or "urgent";
    }

    private static void ThrowIfAny(List<string> errors)
    {
        if (errors.Count > 0)
        {
            throw new AppValidationException(errors.ToArray());
        }
    }
}

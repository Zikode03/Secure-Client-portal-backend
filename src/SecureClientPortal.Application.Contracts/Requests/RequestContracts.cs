namespace SecureClientPortal.Backend.Application.Contracts;

public record CreateRequestRequest(
    string ClientId,
    string RequestType,
    string Title,
    string Description,
    string Priority,
    DateTime? DueDateUtc,
    string? RelatedDocumentId);

public record AddRequestCommentRequest(string Message);
public record UpdateRequestStatusRequest(string Status);
public record ResolveRequestRequest(string? ResolutionNote);

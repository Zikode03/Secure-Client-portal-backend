namespace SecureClientPortal.Backend.Application.Contracts;

public record CreateRequestRequest(
    Guid ClientId,
    string RequestType,
    string Title,
    string Description,
    string Priority,
    DateTime? DueDateUtc,
    Guid? RelatedDocumentId);

public record UpdateRequestRequest(
    string RequestType,
    string Title,
    string Description,
    string Priority,
    DateTime? DueDateUtc,
    Guid? RelatedDocumentId,
    string Status);

public record AddRequestCommentRequest(string Message);
public record UpdateRequestStatusRequest(string Status);
public record ResolveRequestRequest(string? ResolutionNote);

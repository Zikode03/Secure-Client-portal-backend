namespace SecureClientPortal.Backend.Application.Contracts;

public record CreateAssignmentRequest(Guid AccountantUserId, Guid ClientId, bool IsPrimary);
public record ReassignAccountantRequest(Guid ClientId, Guid FromAccountantUserId, Guid ToAccountantUserId, bool MakePrimary);

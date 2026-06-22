namespace SecureClientPortal.Backend.Application.Contracts;

public record CreateAssignmentRequest(string AccountantUserId, string ClientId, bool IsPrimary);
public record ReassignAccountantRequest(string ClientId, string FromAccountantUserId, string ToAccountantUserId, bool MakePrimary);

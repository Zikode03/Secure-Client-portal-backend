namespace SecureClientPortal.Backend.Models;

public sealed record WorkflowActorContext(
    Guid? UserId,
    string RoleScope,
    IReadOnlyList<string> Permissions)
{
    public bool IsAdmin => string.Equals(RoleScope, "admin", StringComparison.OrdinalIgnoreCase);
    public bool IsAccountant => string.Equals(RoleScope, "accountant", StringComparison.OrdinalIgnoreCase);
    public bool IsClient => string.Equals(RoleScope, "client", StringComparison.OrdinalIgnoreCase);

    public static WorkflowActorContext Create(
        Guid? userId,
        string roleScope,
        IReadOnlyList<string>? permissions = null)
    {
        return new WorkflowActorContext(
            userId,
            string.IsNullOrWhiteSpace(roleScope) ? "client" : roleScope.Trim().ToLowerInvariant(),
            permissions ?? []);
    }
}

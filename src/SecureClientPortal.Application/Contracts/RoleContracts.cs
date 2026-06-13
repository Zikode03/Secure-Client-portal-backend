namespace SecureClientPortal.Backend.Application.Contracts;

public record CreateRoleRequest(string Name, string DisplayName, string Scope, string[]? Permissions);
public record UpdateRoleRequest(string? DisplayName, string Scope, string[]? Permissions);

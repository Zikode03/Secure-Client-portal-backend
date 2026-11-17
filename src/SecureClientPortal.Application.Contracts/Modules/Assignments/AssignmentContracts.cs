namespace SecureClientPortal.Backend.Application.Contracts.Modules.Assignments;

public record AssignmentHandoverRequest(string Reason, string Message);
public record CreateAssignmentRequest(Guid AccountantUserId, Guid ClientId, bool IsPrimary, AssignmentHandoverRequest? Handover = null);
public record ReassignAccountantRequest(Guid ClientId, Guid FromAccountantUserId, Guid ToAccountantUserId, bool MakePrimary, AssignmentHandoverRequest? Handover = null);

public sealed record AssignmentResponse(
    [property: System.Text.Json.Serialization.JsonPropertyName("id")] Guid Id,
    [property: System.Text.Json.Serialization.JsonPropertyName("clientId")] Guid ClientId,
    [property: System.Text.Json.Serialization.JsonPropertyName("clientName")] string? ClientName,
    [property: System.Text.Json.Serialization.JsonPropertyName("accountantUserId")] Guid AccountantUserId,
    [property: System.Text.Json.Serialization.JsonPropertyName("accountantName")] string? AccountantName,
    [property: System.Text.Json.Serialization.JsonPropertyName("isPrimary")] bool IsPrimary,
    [property: System.Text.Json.Serialization.JsonPropertyName("createdAtUtc")] DateTime CreatedAtUtc);

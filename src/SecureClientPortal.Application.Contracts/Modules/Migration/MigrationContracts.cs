namespace SecureClientPortal.Backend.Application.Contracts.Modules.Migration;
public sealed record MigrationClientInput(string Name, string EntityType, string Industry, string ContactName,
    string ContactEmail, string RegistrationNumber, string AccountantEmail);
public sealed record MigrationClientRow(Guid Id, int SourceRow, MigrationClientInput Values, bool Skip,
    Guid? ImportedId, string[] Errors);
public sealed record MigrationDocumentInput(Guid? ClientId, int? Year, int? Month, string Category,
    string DisplayName, bool OwnershipConfirmed, bool DuplicateAcknowledged, bool Skip);
public sealed record MigrationDocumentRow(Guid Id, string FileName, long SizeBytes, MigrationDocumentInput Values,
    Guid? ImportedId, string[] Errors, string[] Warnings);
public sealed record MigrationSummary(Guid Id, string Name, string Status, int Version, DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc, int ClientRows, int Documents, int ImportedClients, int ImportedDocuments);
public sealed record MigrationBatchDetail(MigrationSummary Summary, MigrationClientRow[] Clients, MigrationDocumentRow[] Documents);
public sealed record CreateMigrationRequest(Guid RequestId, string Name);
public sealed record EditMigrationClientRequest(int Version, MigrationClientInput Values, bool Skip);
public sealed record EditMigrationDocumentRequest(int Version, MigrationDocumentInput Values);
public sealed record ApproveMigrationRequest(int Version, bool ConfirmOwnershipAndPeriods);

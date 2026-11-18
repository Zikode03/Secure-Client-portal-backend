namespace SecureClientPortal.Backend.Application.Contracts.Modules.Migration;
public sealed record MigrationClientInput(string Name, string EntityType, string Industry, string ContactName,
    string ContactEmail, string RegistrationNumber, string AccountantEmail);
public sealed record MigrationClientRow(Guid Id, int SourceRow, MigrationClientInput Values, bool Skip,
    Guid? ImportedId, string[] Errors, Guid? ExistingClientId = null, bool UseExistingClient = false);
public sealed record MigrationDocumentInput(Guid? ClientId, int? Year, int? Month, string Category,
    string DisplayName, bool OwnershipConfirmed, bool DuplicateAcknowledged, bool Skip,
    bool PreviouslyFiled = false, bool HistoricalStatusConfirmed = false, string? PreviousReviewer = null,
    DateTime? PreviousReviewDateUtc = null, string? HistoricalNotes = null, string? SourcePath = null, string RecordScope = "monthly");
public sealed record MigrationDocumentRow(Guid Id, string FileName, long SizeBytes, MigrationDocumentInput Values,
    Guid? ImportedId, string[] Errors, string[] Warnings);
public sealed record MigrationSummary(Guid Id, string Name, string Status, int Version, DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc, int ClientRows, int Documents, int ImportedClients, int ImportedDocuments, int LinkedClients = 0);
public sealed record MigrationBatchDetail(MigrationSummary Summary, MigrationClientRow[] Clients, MigrationDocumentRow[] Documents, string[]? ArchiveWarnings = null);
public sealed record CreateMigrationRequest(Guid RequestId, string Name);
public sealed record EditMigrationClientRequest(int Version, MigrationClientInput Values, bool Skip, bool UseExistingClient = false);
public sealed record EditMigrationDocumentRequest(int Version, MigrationDocumentInput Values);
public sealed record ApproveMigrationRequest(int Version, bool ConfirmOwnershipAndPeriods);

public sealed record BulkMigrationDocumentRequest(int Version, Guid ClientId, int? Year, int? Month,
    string? Category, bool ConfirmOwnershipAndPeriods, bool PreviouslyFiled, bool HistoricalStatusConfirmed,
    string? PreviousReviewer, DateTime? PreviousReviewDateUtc, string? HistoricalNotes, string RecordScope = "monthly");

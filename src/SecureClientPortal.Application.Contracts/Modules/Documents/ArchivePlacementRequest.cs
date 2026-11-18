namespace SecureClientPortal.Backend.Application.Contracts.Modules.Documents;
public sealed record ArchivePlacementRequest(string Scope,int? Year,int? Month,int ExpectedDocumentVersion);
public sealed record ArchivePlacementResponse(string Scope,int? Year,int? Month);

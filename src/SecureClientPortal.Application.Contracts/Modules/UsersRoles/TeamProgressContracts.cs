namespace SecureClientPortal.Backend.Application.Contracts.Modules.UsersRoles;
public sealed record TeamActivity(string Action, DateTime AtUtc, string? ClientName, string? DocumentName);
public sealed record PackProgress(Guid PackId, int Year, int Month, string Status, int RequiredItems, int ApprovedItems, int AwaitingReview, int InReview, int WaitingOnClient);
public sealed record ClientWorkProgress(Guid ClientId, string ClientName, string Assignment, IReadOnlyList<PackProgress> Packs);
public sealed record AccountantProgress(Guid AccountantId, string Name, string AccountStatus, int PrimaryClients, int BackupClients, int AwaitingReview, int InReview, int RequiredItems, int ApprovedItems, int WaitingOnClient, int DecisionsToday, TeamActivity? LatestActivity, IReadOnlyList<ClientWorkProgress> Clients);
public sealed record TeamProgressResponse(DateTime CheckedAtUtc, int UnassignedClients, IReadOnlyList<AccountantProgress> Accountants);

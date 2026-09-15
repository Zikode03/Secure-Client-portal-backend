namespace SecureClientPortal.Backend.Application.Contracts.Modules.Clients;

public sealed record OnboardClientRequest(Guid RequestId, string Name, string EntityType, string Industry,
    string ContactName, string ContactEmail, Guid AccountantUserId, Guid? ExistingClientUserId,
    string? RegistrationNumber = null);
public sealed record OnboardingPerson(Guid Id, string FullName, string Email);
public sealed record ClientOnboardingOptions(OnboardingPerson[] Accountants, OnboardingPerson[] ClientUsers);
public sealed record ClientOnboardingResponse(Guid ClientId, string ClientName, Guid UserId,
    Guid AccountantUserId, bool UserCreated, string InvitationDelivery, string Message);


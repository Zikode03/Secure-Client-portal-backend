namespace SecureClientPortal.Backend.Application.Contracts.Modules.Clients;

public record UpdateClientStatusRequest(string Status);

public record ClientBusinessProfileResponse(
    Guid ClientId,
    string LegalName,
    string TradingName,
    string RegistrationNumber,
    string TaxNumber,
    string VatNumber,
    string PrimaryContact,
    string FinanceEmail,
    string Phone,
    string AddressLine,
    string City,
    string Country,
    string Industry,
    string PrimaryContactJobTitle,
    DateTime UpdatedAtUtc);

public record UpdateClientBusinessProfileRequest(
    string LegalName,
    string TradingName,
    string RegistrationNumber,
    string TaxNumber,
    string VatNumber,
    string PrimaryContact,
    string FinanceEmail,
    string Phone,
    string AddressLine,
    string City,
    string Country,
    string Industry,
    string PrimaryContactJobTitle);

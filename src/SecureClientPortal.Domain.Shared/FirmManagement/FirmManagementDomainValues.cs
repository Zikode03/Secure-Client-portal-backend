namespace SecureClientPortal.Backend.Models;

public enum ClientStatus { Active, Inactive, Prospect, Archived }

public static class FirmManagementDomainValues
{
    public static string ToStorageValue(this ClientStatus status) => status switch
    {
        ClientStatus.Inactive => "inactive",
        ClientStatus.Prospect => "prospect",
        ClientStatus.Archived => "archived",
        _ => "active"
    };

    public static ClientStatus ToClientStatus(string raw) => (raw?.Trim().ToLowerInvariant()) switch
    {
        "inactive" => ClientStatus.Inactive,
        "prospect" => ClientStatus.Prospect,
        "archived" => ClientStatus.Archived,
        _ => ClientStatus.Active
    };
}

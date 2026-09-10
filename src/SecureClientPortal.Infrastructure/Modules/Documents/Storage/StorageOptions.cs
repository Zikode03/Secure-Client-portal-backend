namespace SecureClientPortal.Backend.Infrastructure.Modules.Documents.Storage;

public sealed class StorageOptions
{
    public const string Section = "Storage";

    public string RootPath { get; set; } = "App_Data/uploads";
    public string KeyRingPath { get; set; } = "App_Data/keyring";
}

namespace SecureClientPortal.Backend.Storage;

public sealed class StorageOptions
{
    public const string Section = "Storage";

    public string RootPath { get; set; } = "App_Data/uploads";
}

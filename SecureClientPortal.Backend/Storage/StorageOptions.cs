namespace SecureClientPortal.Backend.Storage;

public class StorageOptions
{
    public const string Section = "Storage";

    public string Provider { get; set; } = "local";

    public string LocalRoot { get; set; } = "storage/uploads";
}

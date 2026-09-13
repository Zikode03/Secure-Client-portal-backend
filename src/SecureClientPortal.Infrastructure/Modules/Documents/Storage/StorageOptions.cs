namespace SecureClientPortal.Backend.Infrastructure.Modules.Documents.Storage;

public sealed class StorageOptions
{
    public const string Section = "Storage";

    public string? VolumeId { get; set; }
    public string Provider { get; set; } = "local";
    public string? CertificatePath { get; set; }
    public string? CertificatePassword { get; set; }
    public string[] PreviousCertificatePasswords { get; set; } = [];
    public string[] PreviousCertificatePaths { get; set; } = [];
    public string Scanner { get; set; } = "clamav";
    public string ClamAvHost { get; set; } = "127.0.0.1";
    public int ClamAvPort { get; set; } = 3310;
    public int ScanTimeoutSeconds { get; set; } = 120;
    public long MaxFileBytes { get; set; } = 100_000_000;
    public long ClientQuotaBytes { get; set; } = 5_000_000_000;
    public long TotalQuotaBytes { get; set; } = 100_000_000_000;
    public int DownloadsPerMinute { get; set; } = 30;
    public string RootPath { get; set; } = "App_Data/uploads";
    public string KeyRingPath { get; set; } = "App_Data/keyring";
}

using Microsoft.AspNetCore.DataProtection;
using SecureClientPortal.Backend.Infrastructure.Modules.Documents.Storage;
using System.Security.Cryptography.X509Certificates;

namespace SecureClientPortal.Backend.Api.Configuration;
public static class DocumentInfrastructure
{
    public static void Configure(IServiceCollection services, StorageOptions options, IHostEnvironment env)
    {
        if (options.MaxFileBytes < 1 || options.MaxFileBytes > 100_000_000 ||
            options.ClientQuotaBytes < options.MaxFileBytes || options.TotalQuotaBytes < options.ClientQuotaBytes ||
            options.DownloadsPerMinute is < 1 or > 120 || options.ScanTimeoutSeconds is < 1 or > 300 ||
            options.ClamAvPort is < 1 or > 65535 || string.IsNullOrWhiteSpace(options.ClamAvHost))
            throw new InvalidOperationException("Storage quotas, scan limits or download limits are invalid.");
        if (options.Scanner is not ("clamav" or "baseline"))
            throw new InvalidOperationException("Unknown document scanner.");
        if (!env.IsDevelopment())
        {
            if (options.Provider != "shared" || options.Scanner != "clamav")
                throw new InvalidOperationException("Production requires shared persistent storage and ClamAV.");
            foreach (var path in new[] { options.RootPath, options.KeyRingPath })
            {
                if (!Path.IsPathFullyQualified(path) || !Directory.Exists(path))
                    throw new InvalidOperationException("Provision absolute persistent document and key directories before startup.");
                if (string.IsNullOrWhiteSpace(options.VolumeId) ||
                    !File.Exists(Path.Combine(path, ".portal-volume")) ||
                    File.ReadAllText(Path.Combine(path, ".portal-volume")).Trim() != options.VolumeId)
                    throw new InvalidOperationException("The expected persistent volume is not mounted. Verify the .portal-volume markers.");
                var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                foreach (var appPath in new[] { env.ContentRootPath, AppContext.BaseDirectory })
                    if (full.StartsWith(Path.GetFullPath(appPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                        throw new InvalidOperationException("Persistent data cannot be stored in the deployment directory.");
            }
            foreach (var keyFile in Directory.EnumerateFiles(options.KeyRingPath, "*.xml"))
                if (System.Xml.Linq.XDocument.Load(keyFile).Descendants().Any(node => node.Name.LocalName == "masterKey"))
                    throw new InvalidOperationException("The key ring contains plaintext keys. Rewrap existing keys during maintenance before production startup; never delete them.");
            if (string.IsNullOrWhiteSpace(options.CertificatePath))
                throw new InvalidOperationException("A certificate is required to protect production encryption keys.");
        }
        options.RootPath = Path.GetFullPath(options.RootPath, env.ContentRootPath);
        options.KeyRingPath = Path.GetFullPath(options.KeyRingPath, env.ContentRootPath);
        if (options.RootPath == options.KeyRingPath ||
            options.KeyRingPath.StartsWith(options.RootPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidOperationException("Keep the encryption key ring separate from document storage.");
        Directory.CreateDirectory(options.KeyRingPath);
        var builder = services.AddDataProtection().SetApplicationName("SecureClientPortal")
            .PersistKeysToFileSystem(new DirectoryInfo(options.KeyRingPath));
        if (!string.IsNullOrWhiteSpace(options.CertificatePath))
        {
            var certificate = new X509Certificate2(options.CertificatePath, options.CertificatePassword, X509KeyStorageFlags.EphemeralKeySet);
            if (!certificate.HasPrivateKey || certificate.NotAfter.ToUniversalTime() <= DateTime.UtcNow || certificate.NotBefore.ToUniversalTime() > DateTime.UtcNow)
                throw new InvalidOperationException("The key-protection certificate must have a private key and be valid.");
            builder.ProtectKeysWithCertificate(certificate);
            var oldCertificates = options.PreviousCertificatePaths.Select((path, index) =>
                new X509Certificate2(path, options.PreviousCertificatePasswords.ElementAtOrDefault(index) ?? options.CertificatePassword, X509KeyStorageFlags.EphemeralKeySet)).ToArray();
            builder.UnprotectKeysWithAnyCertificate(new[] { certificate }.Concat(oldCertificates).ToArray());
        }
        services.PostConfigure<StorageOptions>(value => {
            value.RootPath = options.RootPath; value.KeyRingPath = options.KeyRingPath;
        });
    }
}

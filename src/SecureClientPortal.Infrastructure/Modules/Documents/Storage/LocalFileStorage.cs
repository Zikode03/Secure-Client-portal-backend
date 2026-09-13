using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using SecureClientPortal.Backend.Application.Modules.Documents;
using System.Security.Cryptography;

namespace SecureClientPortal.Backend.Infrastructure.Modules.Documents.Storage;

public sealed class LocalFileStorage : IFileStorage
{
    private static readonly byte[] EncryptedFileMarker = "SCP1"u8.ToArray();
    private readonly StorageOptions _options;
    private readonly IDataProtector _protector;
    private readonly IDataProtector _scanProtector;
    private readonly IFileSecurityScanner _scanner;

    public LocalFileStorage(
        IOptions<StorageOptions> options,
        IDataProtectionProvider dataProtectionProvider,
        IFileSecurityScanner scanner)
    {
        _options = options.Value;
        _protector = dataProtectionProvider.CreateProtector("SecureClientPortal.DocumentStorage.v1");
        _scanner = scanner;
        _scanProtector = dataProtectionProvider.CreateProtector("SecureClientPortal.DocumentScan.v1");
    }

    public async Task<StoredFile> SaveAsync(IFormFile file, string clientId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (!Guid.TryParse(clientId, out var parsedClient)) throw new InvalidOperationException("A valid client id is required.");
        var originalFileName = Path.GetFileName(file.FileName);
        var rootPath = ResolveRootPath();
        Directory.CreateDirectory(rootPath);
        // FileShare.None is coordinated by the shared filesystem across API replicas.
        await using var quotaGate = await AcquireQuotaLockAsync(rootPath, ct);
        var clientPath = ResolveChildPath(rootPath, parsedClient.ToString());
        Directory.CreateDirectory(clientPath);
        var quarantine = ResolveChildPath(clientPath, ".quarantine");
        Directory.CreateDirectory(quarantine);
        var extension = Path.GetExtension(originalFileName).ToLowerInvariant();
        var storedFileName = $"{Guid.NewGuid():N}{extension}.protected";
        var temporaryPath = ResolveChildPath(quarantine, storedFileName + ".pending");
        var fullPath = ResolveChildPath(clientPath, storedFileName);
        var clientUsed = UsedBytes(clientPath);
        var totalUsed = UsedBytes(rootPath);
        if (clientUsed >= _options.ClientQuotaBytes || totalUsed >= _options.TotalQuotaBytes)
            throw new SecureClientPortal.Backend.Application.Common.AppValidationException("Document storage quota reached.");
        var max = Math.Min(_options.MaxFileBytes, Math.Min(_options.ClientQuotaBytes - clientUsed, _options.TotalQuotaBytes - totalUsed));
        long length;
        try
        {
            await using (var incoming = file.OpenReadStream())
            await using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
                length = await ProtectedDocumentStream.WriteAsync(incoming, output, _protector, max, ct);
            if (length == 0) throw new SecureClientPortal.Backend.Application.Common.AppValidationException("Empty documents cannot be uploaded.");
            if (UsedBytes(clientPath) > _options.ClientQuotaBytes || UsedBytes(rootPath) > _options.TotalQuotaBytes)
                throw new SecureClientPortal.Backend.Application.Common.AppValidationException("Document storage quota reached.");
            await using (var content = await OpenProtectedAsync(temporaryPath, ct))
                await _scanner.ValidateStreamAsync(originalFileName, file.ContentType, content, ct);
            // Only a clean, completely scanned file becomes addressable as a document.
            File.Move(temporaryPath, fullPath);
            try {
                await RecordCleanScanAsync(fullPath, ct);
                if (UsedBytes(clientPath) > _options.ClientQuotaBytes || UsedBytes(rootPath) > _options.TotalQuotaBytes)
                    throw new SecureClientPortal.Backend.Application.Common.AppValidationException("Document storage quota reached.");
            }
            catch { File.Delete(fullPath); if (File.Exists(fullPath + ".scan")) File.Delete(fullPath + ".scan"); throw; }
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
        return new StoredFile(Path.GetRelativePath(rootPath, fullPath).Replace('\\', '/'),
            originalFileName, storedFileName, ContentTypeFor(extension), length);
    }

    private Task<Stream> OpenProtectedAsync(string path, CancellationToken ct) =>
        ProtectedDocumentStream.OpenAsync(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true), _protector, ct);

    private static long UsedBytes(string path) => Directory.EnumerateFiles(path, "*", new EnumerationOptions {
        RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false
    }).Where(path => !path.EndsWith(".quota.lock", StringComparison.Ordinal)).Sum(path => new FileInfo(path).Length);

    private static async Task<FileStream> AcquireQuotaLockAsync(string root, CancellationToken ct)
    {
        var timeout = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try { return new FileStream(ResolveChildPath(root, ".quota.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (DateTime.UtcNow < timeout) { await Task.Delay(100, ct); }
        }
    }

    public async Task<StoredFileContent?> OpenReadAsync(string storageKey, CancellationToken ct = default)
    {
        var rootPath = ResolveRootPath();
        var fullPath = ResolveChildPath(rootPath, storageKey.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath))
        {
            return null;
        }

        if (storageKey.Replace('\\', '/').Split('/').Any(part => part.StartsWith('.')))
            throw new InvalidOperationException("Quarantined documents are not available.");
        await using (var probe = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true))
        {
            var marker = new byte[4];
            await probe.ReadExactlyAsync(marker, ct);
            if (marker.AsSpan().SequenceEqual(ProtectedDocumentStream.Marker))
            {
                var ext = Path.GetExtension(fullPath).Equals(".protected", StringComparison.OrdinalIgnoreCase)
                    ? Path.GetExtension(Path.GetFileNameWithoutExtension(fullPath)).ToLowerInvariant()
                    : Path.GetExtension(fullPath).ToLowerInvariant();
                // Development/baseline SCP2 files must not be mistaken for antivirus-cleared files.
                if (_scanner is ClamAvScanner && !await HasCleanScanAsync(fullPath, ct))
                {
                    await using var untrusted = await OpenProtectedAsync(fullPath, ct);
                    await _scanner.ValidateStreamAsync("document" + ext, ContentTypeFor(ext), untrusted, ct);
                    await RecordCleanScanAsync(fullPath, ct);
                }
                return new StoredFileContent(await OpenProtectedAsync(fullPath, ct), ContentTypeFor(ext));
            }
        }
        // Compatibility path for pre-Phase-4 documents; bounded, rescanned, migrated on read.
        if (new FileInfo(fullPath).Length > _options.MaxFileBytes + 4096)
            throw new InvalidOperationException("Legacy document requires an offline migration.");
        var storedBytes = await File.ReadAllBytesAsync(fullPath, ct);
        byte[] plaintext;
        if (storedBytes.AsSpan().StartsWith(EncryptedFileMarker))
        {
            plaintext = _protector.Unprotect(storedBytes.AsSpan(EncryptedFileMarker.Length).ToArray());
            await _scanner.ValidateAsync(Path.GetFileNameWithoutExtension(fullPath), null, plaintext, ct);
            await ProtectLegacyFileAsync(fullPath, plaintext, ct);
        }
        else
        {
            plaintext = storedBytes;
            await _scanner.ValidateAsync(
                Path.GetFileName(fullPath),
                ContentTypeFor(Path.GetExtension(fullPath).ToLowerInvariant()),
                plaintext,
                ct);
            await ProtectLegacyFileAsync(fullPath, plaintext, ct);
        }

        var storedExtension = Path.GetExtension(fullPath).ToLowerInvariant();
        var originalExtension = storedExtension == ".protected"
            ? Path.GetExtension(Path.GetFileNameWithoutExtension(fullPath)).ToLowerInvariant()
            : storedExtension;
        return new StoredFileContent(new MemoryStream(plaintext, writable: false), ContentTypeFor(originalExtension));
    }

    private async Task ProtectLegacyFileAsync(string fullPath, byte[] plaintext, CancellationToken ct)
    {
        var temporaryPath = fullPath + $".{Guid.NewGuid():N}.tmp";


        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                await using var incoming = new MemoryStream(plaintext, false);
                await ProtectedDocumentStream.WriteAsync(incoming, stream, _protector, _options.MaxFileBytes, ct);
                await stream.FlushAsync(ct);
            }

            File.Move(temporaryPath, fullPath, true);
            await RecordCleanScanAsync(fullPath, ct);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public async Task DeleteAsync(string storageKey, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await using var quotaGate = await AcquireQuotaLockAsync(ResolveRootPath(), ct);
        var fullPath = ResolveChildPath(ResolveRootPath(), storageKey.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }
        if (File.Exists(fullPath + ".scan")) File.Delete(fullPath + ".scan");

        return;
    }

    private string ResolveRootPath()
    {
        var configured = _options.RootPath;
        return Path.GetFullPath(Path.IsPathRooted(configured) ? configured : Path.Combine(AppContext.BaseDirectory, configured));
    }

    private static string ResolveChildPath(string parentPath, string relativePath)
    {
        var fullParent = Path.GetFullPath(parentPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(Path.Combine(fullParent, relativePath));
        if (!candidate.StartsWith(fullParent, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The storage path is outside the configured document root.");
        }

        for (var item = new DirectoryInfo(Path.GetDirectoryName(candidate)!); item is not null; item = item.Parent)
            if (item.Exists && (item.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Storage paths cannot contain links.");
        if (File.Exists(candidate) && (File.GetAttributes(candidate) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Storage paths cannot contain links.");
        return candidate;
    }

    private async Task<string> ScanIdentityAsync(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
        var header = new byte[20]; await stream.ReadExactlyAsync(header, ct);
        // Records authenticate the same random file id; substituting another file/header cannot preserve this binding.
        return Path.GetRelativePath(ResolveRootPath(), path).Replace('\\', '/') + ":" + Convert.ToHexString(header);
    }
    private async Task<bool> HasCleanScanAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path + ".scan")) return false;
        try {
            if (new FileInfo(path + ".scan").Length > 8192) return false;
            var proof = _scanProtector.Unprotect(await File.ReadAllTextAsync(path + ".scan", ct));
            return proof == "clamav:" + await ScanIdentityAsync(path, ct);
        } catch (CryptographicException) { return false; }
    }
    private async Task RecordCleanScanAsync(string path, CancellationToken ct)
    {
        if (_scanner is not ClamAvScanner) return;
        var proof = _scanProtector.Protect("clamav:" + await ScanIdentityAsync(path, ct));
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".scan.tmp";
        try {
            await File.WriteAllTextAsync(temporary, proof, ct);
            File.Move(temporary, path + ".scan", true);
        } finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static string ContentTypeFor(string extension) => extension switch
    {
        ".pdf" => "application/pdf",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".doc" => "application/msword",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".xls" => "application/vnd.ms-excel",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".csv" => "text/csv",
        ".txt" => "text/plain",
        _ => "application/octet-stream"
    };
}

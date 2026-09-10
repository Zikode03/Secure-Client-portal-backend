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
    private readonly IFileSecurityScanner _scanner;

    public LocalFileStorage(
        IOptions<StorageOptions> options,
        IDataProtectionProvider dataProtectionProvider,
        IFileSecurityScanner scanner)
    {
        _options = options.Value;
        _protector = dataProtectionProvider.CreateProtector("SecureClientPortal.DocumentStorage.v1");
        _scanner = scanner;
    }

    public async Task<StoredFile> SaveAsync(IFormFile file, string clientId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        var originalFileName = Path.GetFileName(file.FileName);
        var rootPath = ResolveRootPath();
        var clientPath = ResolveChildPath(rootPath, Sanitize(clientId));
        Directory.CreateDirectory(clientPath);

        await using var incoming = new MemoryStream();
        await file.CopyToAsync(incoming, ct);
        var plaintext = incoming.ToArray();
        await _scanner.ValidateAsync(originalFileName, file.ContentType, plaintext, ct);

        var extension = Path.GetExtension(originalFileName).ToLowerInvariant();
        var storedFileName = $"{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}{extension}.protected";
        var fullPath = ResolveChildPath(clientPath, storedFileName);
        var temporaryPath = fullPath + ".tmp";
        var protectedBytes = _protector.Protect(plaintext);

        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                await stream.WriteAsync(EncryptedFileMarker, ct);
                await stream.WriteAsync(protectedBytes, ct);
                await stream.FlushAsync(ct);
            }
            File.Move(temporaryPath, fullPath);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

        CryptographicOperations.ZeroMemory(plaintext);
        var storageKey = Path.GetRelativePath(rootPath, fullPath).Replace('\\', '/');
        return new StoredFile(
            storageKey,
            originalFileName,
            storedFileName,
            ContentTypeFor(extension),
            file.Length);
    }

    public async Task<StoredFileContent?> OpenReadAsync(string storageKey, CancellationToken ct = default)
    {
        var rootPath = ResolveRootPath();
        var fullPath = ResolveChildPath(rootPath, storageKey.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath))
        {
            return null;
        }

        var storedBytes = await File.ReadAllBytesAsync(fullPath, ct);
        byte[] plaintext;
        if (storedBytes.AsSpan().StartsWith(EncryptedFileMarker))
        {
            plaintext = _protector.Unprotect(storedBytes.AsSpan(EncryptedFileMarker.Length).ToArray());
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
        var protectedBytes = _protector.Protect(plaintext);

        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                await stream.WriteAsync(EncryptedFileMarker, ct);
                await stream.WriteAsync(protectedBytes, ct);
                await stream.FlushAsync(ct);
            }

            File.Move(temporaryPath, fullPath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public Task DeleteAsync(string storageKey, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var fullPath = ResolveChildPath(ResolveRootPath(), storageKey.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        return Task.CompletedTask;
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
        if (!candidate.StartsWith(fullParent, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The storage path is outside the configured document root.");
        }

        return candidate;
    }

    private static string Sanitize(string value)
    {
        var safe = value;
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            safe = safe.Replace(invalid, '_');
        }

        return safe is "" or "." or ".." ? "unknown" : safe;
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

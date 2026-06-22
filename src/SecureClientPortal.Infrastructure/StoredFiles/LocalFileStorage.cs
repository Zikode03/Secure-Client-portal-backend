using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace SecureClientPortal.Backend.Storage;

public sealed class LocalFileStorage : IFileStorage
{
    private readonly StorageOptions _options;

    public LocalFileStorage(IOptions<StorageOptions> options)
    {
        _options = options.Value;
    }

    public async Task<StoredFile> SaveAsync(IFormFile file, string clientId, CancellationToken ct = default)
    {
        var rootPath = ResolveRootPath();
        var clientPath = Path.Combine(rootPath, Sanitize(clientId));
        Directory.CreateDirectory(clientPath);

        var storedFileName = $"{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}{Path.GetExtension(file.FileName)}";
        var fullPath = Path.Combine(clientPath, storedFileName);
        await using (var stream = File.Create(fullPath))
        {
            await file.CopyToAsync(stream, ct);
        }

        var storageKey = Path.GetRelativePath(rootPath, fullPath).Replace('\\', '/');
        return new StoredFile(storageKey, file.FileName, storedFileName, file.ContentType, file.Length);
    }

    public async Task<StoredFileContent?> OpenReadAsync(string storageKey, CancellationToken ct = default)
    {
        var rootPath = ResolveRootPath();
        var fullPath = Path.Combine(rootPath, storageKey.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath))
        {
            return null;
        }

        var memory = new MemoryStream();
        await using (var stream = File.OpenRead(fullPath))
        {
            await stream.CopyToAsync(memory, ct);
        }

        memory.Position = 0;
        return new StoredFileContent(memory, GuessContentType(fullPath));
    }

    private string ResolveRootPath()
    {
        var configured = _options.RootPath;
        return Path.GetFullPath(Path.IsPathRooted(configured) ? configured : Path.Combine(AppContext.BaseDirectory, configured));
    }

    private static string Sanitize(string value)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalid, '_');
        }

        return value;
    }

    private static string GuessContentType(string fullPath)
    {
        return Path.GetExtension(fullPath).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls" => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            _ => "application/octet-stream"
        };
    }
}

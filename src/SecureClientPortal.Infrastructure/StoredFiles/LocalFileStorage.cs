using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace SecureClientPortal.Backend.Storage;

public sealed class LocalFileStorage : IFileStorage
{
    private readonly string _rootPath;

    public LocalFileStorage(IOptions<StorageOptions> options, IWebHostEnvironment environment)
    {
        var configuredRoot = options.Value.RootPath?.Trim();
        var relativeRoot = string.IsNullOrWhiteSpace(configuredRoot) ? "App_Data/uploads" : configuredRoot;
        _rootPath = Path.IsPathRooted(relativeRoot)
            ? relativeRoot
            : Path.Combine(environment.ContentRootPath, relativeRoot);
    }

    public async Task<StoredFile> SaveAsync(IFormFile file, string clientId, CancellationToken ct = default)
    {
        var safeClientId = string.IsNullOrWhiteSpace(clientId) ? "unknown" : clientId.Trim();
        var originalFileName = Path.GetFileName(file.FileName);
        var extension = Path.GetExtension(originalFileName);
        var storedFileName = $"{Guid.NewGuid():N}{extension}";
        var clientFolder = Path.Combine(_rootPath, safeClientId);

        Directory.CreateDirectory(clientFolder);

        var absolutePath = Path.Combine(clientFolder, storedFileName);
        await using (var stream = File.Create(absolutePath))
        {
            await file.CopyToAsync(stream, ct);
        }

        var storageKey = Path.Combine(safeClientId, storedFileName).Replace('\\', '/');
        return new StoredFile(storageKey, originalFileName, storedFileName, file.ContentType, file.Length);
    }

    public Task<StoredFileContent?> OpenReadAsync(string storageKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(storageKey))
        {
            return Task.FromResult<StoredFileContent?>(null);
        }

        var normalizedKey = storageKey.Replace('/', Path.DirectorySeparatorChar);
        var absolutePath = Path.Combine(_rootPath, normalizedKey);
        if (!File.Exists(absolutePath))
        {
            return Task.FromResult<StoredFileContent?>(null);
        }

        Stream stream = new FileStream(
            absolutePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            options: FileOptions.Asynchronous);

        return Task.FromResult<StoredFileContent?>(new StoredFileContent(stream, GetContentType(absolutePath)));
    }

    private static string GetContentType(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
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

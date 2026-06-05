using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Options;

namespace SecureClientPortal.Backend.Storage;

public class LocalFileStorage : IFileStorage
{
    private readonly string _storageRoot;
    private readonly FileExtensionContentTypeProvider _contentTypes = new();

    public LocalFileStorage(IOptions<StorageOptions> options, IWebHostEnvironment environment)
    {
        var configuredRoot = options.Value.LocalRoot;
        var root = string.IsNullOrWhiteSpace(configuredRoot) ? "storage/uploads" : configuredRoot;
        _storageRoot = Path.IsPathRooted(root) ? root : Path.Combine(environment.ContentRootPath, root);
    }

    public async Task<StoredFile> SaveAsync(IFormFile file, string clientId, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_storageRoot);

        var safeClientId = SanitizePathSegment(clientId);
        var extension = Path.GetExtension(file.FileName);
        var storageFileName = $"{Guid.NewGuid():N}{extension}";
        var relativePath = Path.Combine(safeClientId, storageFileName);
        var fullPath = Path.Combine(_storageRoot, relativePath);
        var directory = Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var target = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await file.CopyToAsync(target, ct);

        var storageKey = relativePath.Replace('\\', '/');
        var contentType = string.IsNullOrWhiteSpace(file.ContentType)
            ? (_contentTypes.TryGetContentType(file.FileName, out var detected) ? detected : "application/octet-stream")
            : file.ContentType;
        return new StoredFile(storageKey, Path.GetFileName(file.FileName), storageFileName, contentType, file.Length);
    }

    public Task<StoredFileContent?> OpenReadAsync(string storageKey, CancellationToken ct = default)
    {
        var normalizedKey = storageKey.Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(_storageRoot, normalizedKey));
        var expectedRoot = Path.GetFullPath(_storageRoot);

        if (!fullPath.StartsWith(expectedRoot, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
        {
            return Task.FromResult<StoredFileContent?>(null);
        }

        var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var contentType = _contentTypes.TryGetContentType(fullPath, out var detected)
            ? detected
            : "application/octet-stream";

        return Task.FromResult<StoredFileContent?>(new StoredFileContent(stream, contentType));
    }

    private static string SanitizePathSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Where(ch => !invalid.Contains(ch)).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "unknown" : sanitized;
    }
}

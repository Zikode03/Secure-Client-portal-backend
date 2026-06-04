namespace SecureClientPortal.Backend.Storage;

public interface IFileStorage
{
    Task<StoredFile> SaveAsync(IFormFile file, string clientId, CancellationToken ct = default);

    Task<StoredFileContent?> OpenReadAsync(string storageKey, CancellationToken ct = default);
}

public sealed record StoredFile(string StorageKey, string OriginalFileName, long SizeBytes);

public sealed record StoredFileContent(Stream Stream, string ContentType);

using Microsoft.AspNetCore.Http;

namespace SecureClientPortal.Backend.Application.Documents;

public sealed record StoredFile(
    string StorageKey,
    string OriginalFileName,
    string StoredFileName,
    string ContentType,
    long SizeBytes);

public sealed record StoredFileContent(Stream Stream, string ContentType);

public interface IFileStorage
{
    Task<StoredFile> SaveAsync(IFormFile file, string clientId, CancellationToken ct = default);
    Task<StoredFileContent?> OpenReadAsync(string storageKey, CancellationToken ct = default);
}

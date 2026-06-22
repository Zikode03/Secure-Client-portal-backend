using Microsoft.AspNetCore.Http;

namespace SecureClientPortal.Backend.Storage;

public interface IFileStorage
{
    Task<StoredFile> SaveAsync(IFormFile file, string clientId, CancellationToken ct = default);
    Task<StoredFileContent?> OpenReadAsync(string storageKey, CancellationToken ct = default);
}

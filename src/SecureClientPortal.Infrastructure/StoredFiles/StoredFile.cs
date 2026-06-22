namespace SecureClientPortal.Backend.Storage;

public sealed record StoredFile(
    string StorageKey,
    string OriginalFileName,
    string StoredFileName,
    string ContentType,
    long SizeBytes);

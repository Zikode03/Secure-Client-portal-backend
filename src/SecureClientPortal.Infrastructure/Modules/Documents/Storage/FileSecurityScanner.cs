using SecureClientPortal.Backend.Application.Common;
using System.Text;

namespace SecureClientPortal.Backend.Infrastructure.Modules.Documents.Storage;

public interface IFileSecurityScanner
{
    Task ValidateAsync(string fileName, string? declaredContentType, ReadOnlyMemory<byte> content, CancellationToken ct = default);
    async Task ValidateStreamAsync(string fileName, string? contentType, Stream stream, CancellationToken ct = default)
    {
        var prefix = new byte[16 * 1024];
        var count = await stream.ReadAtLeastAsync(prefix, prefix.Length, throwOnEndOfStream: false, cancellationToken: ct);
        await ValidateAsync(fileName, contentType, prefix.AsMemory(0, count), ct);
    }
}

public sealed class FileSecurityScanner : IFileSecurityScanner
{
    private const string EicarMarker = "EICAR-STANDARD-ANTIVIRUS-TEST-FILE";

    public Task ValidateAsync(
        string fileName,
        string? declaredContentType,
        ReadOnlyMemory<byte> content,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var bytes = content.Span;

        if (!HasExpectedSignature(extension, bytes))
        {
            throw new AppValidationException("The file contents do not match the selected file type.");
        }

        var searchable = Encoding.ASCII.GetString(bytes[..Math.Min(bytes.Length, 16 * 1024)]);
        if (searchable.Contains(EicarMarker, StringComparison.OrdinalIgnoreCase))
        {
            throw new AppValidationException("The file was blocked by the security scanner.");
        }

        return Task.CompletedTask;
    }

    private static bool HasExpectedSignature(string extension, ReadOnlySpan<byte> bytes) => extension switch
    {
        ".pdf" => StartsWith(bytes, "%PDF-"u8),
        ".png" => StartsWith(bytes, new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
        ".jpg" or ".jpeg" => StartsWith(bytes, new byte[] { 0xFF, 0xD8, 0xFF }),
        ".doc" or ".xls" => StartsWith(bytes, new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 }),
        ".docx" or ".xlsx" => StartsWith(bytes, "PK"u8),
        ".csv" or ".txt" => !bytes.Contains((byte)0),
        _ => false
    };

    private static bool StartsWith(ReadOnlySpan<byte> content, ReadOnlySpan<byte> signature) =>
        content.Length >= signature.Length && content[..signature.Length].SequenceEqual(signature);
}

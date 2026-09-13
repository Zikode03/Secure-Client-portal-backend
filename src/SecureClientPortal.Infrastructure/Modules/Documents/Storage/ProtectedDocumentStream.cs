using System.Buffers.Binary;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using SecureClientPortal.Backend.Application.Common;

namespace SecureClientPortal.Backend.Infrastructure.Modules.Documents.Storage;

// Each bounded record authenticates file id, sequence and final marker to prevent
// record reordering, cross-file substitution and undetected truncation.
public sealed class ProtectedDocumentStream : Stream
{
    public static readonly byte[] Marker = "SCP2"u8.ToArray();
    private const int ChunkSize = 64 * 1024;
    private readonly Stream source;
    private readonly IDataProtector protector;
    private readonly byte[] fileId;
    private byte[] current = [];
    private int position;
    private long sequence;
    private bool ended;
    private ProtectedDocumentStream(Stream source, IDataProtector protector, byte[] fileId)
    { this.source = source; this.protector = protector; this.fileId = fileId; }

    public static async Task<long> WriteAsync(Stream source, Stream destination, IDataProtector protector, long maxBytes, CancellationToken ct)
    {
        var id = RandomNumberGenerator.GetBytes(16);
        await destination.WriteAsync(Marker, ct); await destination.WriteAsync(id, ct);
        var buffer = new byte[ChunkSize]; long total = 0, index = 0;
        try {
            while (true)
            {
                var count = await source.ReadAtLeastAsync(buffer, buffer.Length, false, ct);
                total += count;
                if (total > maxBytes) throw new AppValidationException("The document exceeds the maximum file size.");
                var record = new byte[25 + count];
                id.CopyTo(record, 0); BinaryPrimitives.WriteInt64BigEndian(record.AsSpan(16), index++);
                record[24] = count == 0 ? (byte)1 : (byte)0;
                buffer.AsSpan(0, count).CopyTo(record.AsSpan(25));
                byte[] encrypted;
                try { encrypted = protector.Protect(record); } finally { CryptographicOperations.ZeroMemory(record); }
                var length = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(length, encrypted.Length);
                await destination.WriteAsync(length, ct); await destination.WriteAsync(encrypted, ct);
                if (count == 0) break;
            }
            await destination.FlushAsync(ct); return total;
        } finally { CryptographicOperations.ZeroMemory(buffer); }
    }

    public static async Task<Stream> OpenAsync(Stream source, IDataProtector protector, CancellationToken ct)
    {
        try {
            var header = new byte[20]; await source.ReadExactlyAsync(header, ct);
            if (!header.AsSpan(0, 4).SequenceEqual(Marker)) throw new CryptographicException("Invalid document header.");
            return new ProtectedDocumentStream(source, protector, header[4..]);
        } catch { await source.DisposeAsync(); throw; }
    }
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        if (buffer.Length == 0 || ended) return 0;
        if (position == current.Length)
        {
            CryptographicOperations.ZeroMemory(current);
            var lengthBytes = new byte[4]; await source.ReadExactlyAsync(lengthBytes, ct);
            var length = BinaryPrimitives.ReadInt32BigEndian(lengthBytes);
            if (length < 1 || length > ChunkSize + 4096) throw new CryptographicException("Invalid document record.");
            var encrypted = new byte[length]; await source.ReadExactlyAsync(encrypted, ct);
            var record = protector.Unprotect(encrypted);
            if (record.Length < 25 || record.Length > ChunkSize + 25 ||
                !record.AsSpan(0,16).SequenceEqual(fileId) ||
                BinaryPrimitives.ReadInt64BigEndian(record.AsSpan(16,8)) != sequence++)
                throw new CryptographicException("Invalid document sequence.");
            if (record[24] == 1)
            {
                if (record.Length != 25 || await source.ReadAsync(new byte[1], ct) != 0)
                    throw new CryptographicException("Invalid document ending.");
                CryptographicOperations.ZeroMemory(record); ended = true; return 0;
            }
            if (record[24] != 0 || record.Length == 25) throw new CryptographicException("Invalid document record.");
            current = record[25..]; CryptographicOperations.ZeroMemory(record); position = 0;
        }
        var count = Math.Min(buffer.Length, current.Length - position);
        current.AsMemory(position, count).CopyTo(buffer); position += count; return count;
    }
    public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset,count)).AsTask().GetAwaiter().GetResult();
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => ReadAsync(buffer.AsMemory(offset,count), ct).AsTask();
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    protected override void Dispose(bool disposing) { if (disposing) { CryptographicOperations.ZeroMemory(current); source.Dispose(); } base.Dispose(disposing); }
    public override async ValueTask DisposeAsync() { CryptographicOperations.ZeroMemory(current); await source.DisposeAsync(); GC.SuppressFinalize(this); }
}

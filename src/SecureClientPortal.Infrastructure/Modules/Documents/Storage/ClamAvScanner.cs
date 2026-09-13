using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Options;
using SecureClientPortal.Backend.Application.Common;

namespace SecureClientPortal.Backend.Infrastructure.Modules.Documents.Storage;
public sealed class ClamAvScanner(IOptions<StorageOptions> options) : IFileSecurityScanner
{
    public async Task ValidateAsync(string fileName, string? contentType, ReadOnlyMemory<byte> content, CancellationToken ct = default)
    {
        await using var stream = new MemoryStream(content.ToArray(), false);
        await ValidateStreamAsync(fileName, contentType, stream, ct);
    }
    public async Task ValidateStreamAsync(string fileName, string? contentType, Stream stream, CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.ScanTimeoutSeconds));
        var token = timeout.Token;
        using var socket = new TcpClient();
        await socket.ConnectAsync(options.Value.ClamAvHost, options.Value.ClamAvPort, token);
        await using var network = socket.GetStream();
        await network.WriteAsync(Encoding.ASCII.GetBytes("zINSTREAM\0"), token);
        var buffer = new byte[64 * 1024];
        var size = new byte[4];
        long total = 0; bool first = true;
        while (true)
        {
            var count = await stream.ReadAsync(buffer, token);
            if (count == 0) break;
            if (first) { await new FileSecurityScanner().ValidateAsync(fileName, contentType, buffer.AsMemory(0, count), token); first = false; }
            total += count;
            if (total > options.Value.MaxFileBytes) throw new AppValidationException("The document exceeds the maximum file size.");
            BinaryPrimitives.WriteInt32BigEndian(size, count);
            await network.WriteAsync(size, token);
            await network.WriteAsync(buffer.AsMemory(0, count), token);
        }
        if (first) throw new AppValidationException("Empty documents cannot be uploaded.");
        await network.WriteAsync(new byte[4], token);
        var response = new List<byte>();
        while (response.Count < 4096)
        {
            var next = new byte[1];
            if (await network.ReadAsync(next, token) == 0) throw new IOException("Scanner closed without a verdict.");
            if (next[0] == 0) break;
            response.Add(next[0]);
        }
        var verdict = Encoding.UTF8.GetString(response.ToArray()).Trim();
        if (verdict.EndsWith(" FOUND", StringComparison.Ordinal))
            throw new AppValidationException("The document was blocked by antivirus scanning.");
        if (verdict != "stream: OK") throw new IOException("Scanner did not confirm the document is clean.");
    }
}

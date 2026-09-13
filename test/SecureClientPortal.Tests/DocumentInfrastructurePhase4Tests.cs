using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Infrastructure.Modules.Documents.Storage;

namespace SecureClientPortal.Backend.Tests;
public sealed class DocumentInfrastructurePhase4Tests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    [Fact]
    public async Task EncryptedChunksRoundTripLargeStreamAndRejectTamperingAndTruncation()
    {
        var protector = new EphemeralDataProtectionProvider().CreateProtector("test");
        var bytes = RandomNumberGenerator.GetBytes(2_000_000);
        using var output = new MemoryStream();
        Assert.Equal(bytes.Length,await ProtectedDocumentStream.WriteAsync(new MemoryStream(bytes),output,protector,3_000_000,Ct));
        var encrypted = output.ToArray();
        await using (var reader = await ProtectedDocumentStream.OpenAsync(new MemoryStream(encrypted),protector,Ct))
        {
            using var decoded = new MemoryStream(); await reader.CopyToAsync(decoded,Ct);
            Assert.Equal(bytes,decoded.ToArray()); Assert.False(reader.CanSeek);
        }
        encrypted[100] ^= 1;
        await using (var reader = await ProtectedDocumentStream.OpenAsync(new MemoryStream(encrypted),protector,Ct))
            await Assert.ThrowsAsync<CryptographicException>(()=>reader.CopyToAsync(Stream.Null,Ct));
        var truncated = output.ToArray()[..^12];
        await using (var reader = await ProtectedDocumentStream.OpenAsync(new MemoryStream(truncated),protector,Ct))
            await Assert.ThrowsAsync<EndOfStreamException>(()=>reader.CopyToAsync(Stream.Null,Ct));
    }
    [Fact]
    public async Task OversizedUploadsStopAtBoundedChunk()
    {
        var protector = new EphemeralDataProtectionProvider().CreateProtector("test");
        using var output = new MemoryStream();
        await Assert.ThrowsAsync<AppValidationException>(()=>ProtectedDocumentStream.WriteAsync(new MemoryStream(new byte[100_000]),output,protector,50_000,Ct));
    }
    [Theory]
    [InlineData("stream: OK\0", true)]
    [InlineData("stream: Eicar-Test-Signature FOUND\0", false)]
    [InlineData("stream: scanning ERROR\0", false)]
    public async Task ClamAvStreamsEveryByteAndOnlyAcceptsCleanVerdict(string verdict,bool clean)
    {
        using var listener = new TcpListener(IPAddress.Loopback,0); listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var bytes = Encoding.ASCII.GetBytes("%PDF-1.7\n" + new string('a',200_000));
        var server = Task.Run(async ()=>{
            using var client = await listener.AcceptTcpClientAsync(Ct);
            using var stream = client.GetStream();
            var command = new byte[10]; await stream.ReadExactlyAsync(command,Ct);
            Assert.Equal("zINSTREAM\0",Encoding.ASCII.GetString(command));
            using var received = new MemoryStream();
            while(true) {
                var size = new byte[4]; await stream.ReadExactlyAsync(size,Ct);
                var length = BinaryPrimitives.ReadInt32BigEndian(size); if(length==0)break;
                Assert.InRange(length,1,65536);
                var chunk = new byte[length]; await stream.ReadExactlyAsync(chunk,Ct); await received.WriteAsync(chunk,Ct);
            }
            Assert.Equal(bytes,received.ToArray());
            await stream.WriteAsync(Encoding.ASCII.GetBytes(verdict),Ct);
        },Ct);
        var scanner = new ClamAvScanner(Options.Create(new StorageOptions { ClamAvHost="127.0.0.1",ClamAvPort=port }));
        if(clean) await scanner.ValidateStreamAsync("test.pdf","application/pdf",new MemoryStream(bytes),Ct);
        else await Assert.ThrowsAnyAsync<Exception>(()=>scanner.ValidateStreamAsync("test.pdf","application/pdf",new MemoryStream(bytes),Ct));
        await server;
    }
    [Fact]
    public async Task ScannerFailureNeverPublishesAndQuotaRejectsWithoutLeavingPartialFiles()
    {
        var root=Path.Combine(Path.GetTempPath(),"scp-quarantine-"+Guid.NewGuid().ToString("N"));
        try {
            var options=Options.Create(new StorageOptions{RootPath=root});
            var storage=new LocalFileStorage(options,new EphemeralDataProtectionProvider(),new RejectScanner());
            await Assert.ThrowsAsync<IOException>(()=>storage.SaveAsync(File(),Guid.NewGuid().ToString(),Ct));
            Assert.Empty(Directory.EnumerateFiles(root,"*.protected",SearchOption.AllDirectories));
            Assert.Empty(Directory.EnumerateFiles(root,"*.pending",SearchOption.AllDirectories));
            options.Value.TotalQuotaBytes=1; options.Value.ClientQuotaBytes=1;
            await Assert.ThrowsAsync<AppValidationException>(()=>storage.SaveAsync(File(),Guid.NewGuid().ToString(),Ct));
            Assert.Empty(Directory.EnumerateFiles(root,"*.protected",SearchOption.AllDirectories));
        } finally { if(Directory.Exists(root))Directory.Delete(root,true); }
    }
    [Fact]
    public async Task CertificateProtectedKeysAndDocumentsSurviveNewProviderAndBackupRestore()
    {
        var root=Path.Combine(Path.GetTempPath(),"scp-restore-"+Guid.NewGuid().ToString("N"));
        try {
            Directory.CreateDirectory(root);
            using var rsa=RSA.Create(2048);
            using var certificate=new CertificateRequest("CN=Portal test",rsa,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1)
                .CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1),DateTimeOffset.UtcNow.AddDays(1));
            var keys=Path.Combine(root,"keys"); Directory.CreateDirectory(keys);
            IDataProtectionProvider Provider(string path)=>DataProtectionProvider.Create(new DirectoryInfo(path),config=>config.SetApplicationName("SecureClientPortal").ProtectKeysWithCertificate(certificate));
            var documents=Path.Combine(root,"documents");
            var storage=new LocalFileStorage(Options.Create(new StorageOptions{RootPath=documents}),Provider(keys),new FileSecurityScanner());
            var saved=await storage.SaveAsync(File(),Guid.NewGuid().ToString(),Ct);
            foreach(var file in Directory.GetFiles(keys,"*.xml")) {
                var xml=await System.IO.File.ReadAllTextAsync(file,Ct);
                Assert.Contains("encryptedSecret",xml); Assert.DoesNotContain("<masterKey",xml);
            }
            var restoredKeys=Path.Combine(root,"restored-keys"); Directory.CreateDirectory(restoredKeys);
            foreach(var file in Directory.GetFiles(keys))System.IO.File.Copy(file,Path.Combine(restoredKeys,Path.GetFileName(file)));
            var restoredDocs=Path.Combine(root,"restored-documents");
            foreach(var file in Directory.GetFiles(documents,"*.protected",SearchOption.AllDirectories)) {
                var dest=Path.Combine(restoredDocs,Path.GetRelativePath(documents,file));
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);System.IO.File.Copy(file,dest);
            }
            var restored=new LocalFileStorage(Options.Create(new StorageOptions{RootPath=restoredDocs}),Provider(restoredKeys),new FileSecurityScanner());
            var content=await restored.OpenReadAsync(saved.StorageKey,Ct);
            using var reader=new StreamReader(content!.Stream);
            Assert.Equal("%PDF-1.7\nconfidential\n%%EOF",await reader.ReadToEndAsync(Ct));
        } finally { if(Directory.Exists(root))Directory.Delete(root,true); }
    }
    private static IFormFile File()
    {
        var stream=new MemoryStream("%PDF-1.7\nconfidential\n%%EOF"u8.ToArray());
        return new FormFile(stream,0,stream.Length,"file","document.pdf"){Headers=new HeaderDictionary(),ContentType="application/pdf"};
    }
    private sealed class RejectScanner:IFileSecurityScanner
    {
        public Task ValidateAsync(string fileName,string? type,ReadOnlyMemory<byte> content,CancellationToken ct=default)=>throw new IOException("Scanner unavailable");
    }
}

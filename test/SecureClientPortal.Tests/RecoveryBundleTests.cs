using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using SecureClientPortal.Backend.Infrastructure.Modules.Documents.Storage;
using SecureClientPortal.Backend.Infrastructure.Recovery;
using Xunit;

namespace SecureClientPortal.Backend.Tests;
public sealed class RecoveryBundleTests
{
    private sealed class Fixture : IDisposable
    {
        public string Root=RecoveryBundle.PrivateDirectory(Path.GetTempPath());
        public string Password="Isolated backup passphrase for automated tests only";
        public string CertificatePassword=Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
        public string Bundle=>Path.Combine(Root,"full.scpbak");
        public async Task CreateAsync()
        {
            var docs=Path.Combine(Root,"docs");var keys=Path.Combine(Root,"keys");Directory.CreateDirectory(docs);Directory.CreateDirectory(keys);
            using var rsa=RSA.Create(2048);
            using var cert=new CertificateRequest("CN=Recovery test",rsa,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1).CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow.AddDays(2));
            var certFile=Path.Combine(Root,"key.pfx");File.WriteAllBytes(certFile,cert.Export(X509ContentType.Pfx,CertificatePassword));
            var provider=DataProtectionProvider.Create(new DirectoryInfo(keys),b=>b.SetApplicationName("SecureClientPortal").ProtectKeysWithCertificate(cert));
            var storage=new LocalFileStorage(Options.Create(new StorageOptions {RootPath=docs}),provider,new FileSecurityScanner());
            var bytes=Encoding.UTF8.GetBytes("%PDF-1.7\nIsolated private recovery proof\n%%EOF");
            await storage.SaveAsync(new FormFile(new MemoryStream(bytes),0,bytes.Length,"file","proof.pdf"){Headers=new HeaderDictionary(),ContentType="application/pdf"},Guid.NewGuid().ToString(),TestContext.Current.CancellationToken);
            var sql=Path.Combine(Root,"source.bak");File.WriteAllText(sql,"isolated SQL-backup byte fixture");
            await RecoveryBundle.CreateAsync(Bundle,"isolated",sql,docs,keys,[certFile],Password,[CertificatePassword],TestContext.Current.CancellationToken);
        }
        public void Dispose()=>Directory.Delete(Root,true);
    }
    [Fact] public async Task Full_bundle_restores_database_bytes_documents_keys_and_private_certificates()
    {
        using var fixture=new Fixture();await fixture.CreateAsync();
        var destination=Path.Combine(fixture.Root,"restore");
        var manifest=await RecoveryBundle.ExtractAsync(fixture.Bundle,destination,fixture.Password,[fixture.CertificatePassword],TestContext.Current.CancellationToken);
        Assert.Equal("isolated",manifest.Database);
        if(!OperatingSystem.IsWindows()) Assert.Equal((UnixFileMode)0, File.GetUnixFileMode(destination) & (UnixFileMode.GroupRead|UnixFileMode.GroupWrite|UnixFileMode.GroupExecute|UnixFileMode.OtherRead|UnixFileMode.OtherWrite|UnixFileMode.OtherExecute));
        Assert.Equal("isolated SQL-backup byte fixture",File.ReadAllText(Path.Combine(destination,"Database","database.bak")));
        Assert.Contains(manifest.Files,x=>x.Path.StartsWith("Documents/")&&x.Path.EndsWith(".protected"));
        Assert.Contains(manifest.Files,x=>x.Path.StartsWith("KeyRing/")&&x.Path.EndsWith(".xml"));
        Assert.True(File.Exists(Path.Combine(destination,"Certificates","0.pfx")));
        Assert.DoesNotContain("Isolated private recovery proof",Encoding.UTF8.GetString(File.ReadAllBytes(fixture.Bundle)));
    }
    [Theory]
    [InlineData("wrong-password")]
    [InlineData("wrong-certificate-password")]
    [InlineData("tampered")]
    [InlineData("truncated")]
    public async Task Invalid_backups_never_publish_a_restore_destination(string failure)
    {
        using var fixture=new Fixture();await fixture.CreateAsync();
        if(failure is "tampered" or "truncated") {var bytes=File.ReadAllBytes(fixture.Bundle);if(failure=="tampered")bytes[40]^=1;else Array.Resize(ref bytes,bytes.Length-16);File.WriteAllBytes(fixture.Bundle,bytes);}
        var destination=Path.Combine(fixture.Root,"failed");
        await Assert.ThrowsAnyAsync<Exception>(()=>RecoveryBundle.ExtractAsync(fixture.Bundle,destination,failure=="wrong-password"?"wrong":fixture.Password,[failure=="wrong-certificate-password"?"wrong":fixture.CertificatePassword],TestContext.Current.CancellationToken));
        Assert.False(Directory.Exists(destination));
    }
    [Fact] public async Task Existing_destination_is_never_overwritten()
    {
        using var fixture=new Fixture();await fixture.CreateAsync();var destination=Path.Combine(fixture.Root,"existing");Directory.CreateDirectory(destination);File.WriteAllText(Path.Combine(destination,"proof"),"keep");
        await Assert.ThrowsAsync<IOException>(()=>RecoveryBundle.ExtractAsync(fixture.Bundle,destination,fixture.Password,[fixture.CertificatePassword],TestContext.Current.CancellationToken));
        Assert.Equal("keep",File.ReadAllText(Path.Combine(destination,"proof")));
    }
    [Theory]
    [InlineData("../outside")]
    [InlineData("/absolute")]
    [InlineData("folder\\..\\outside")]
    [InlineData("C:/outside")]
    public void Unsafe_archive_paths_are_rejected(string entry)
    {Assert.ThrowsAny<Exception>(()=>RecoveryBundle.SafeChild(Path.GetTempPath(),entry));}
}

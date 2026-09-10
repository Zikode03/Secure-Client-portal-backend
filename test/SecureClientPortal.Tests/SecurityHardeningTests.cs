using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Infrastructure.Modules.Documents.Storage;
using System.Security.Cryptography;
using System.Text;

namespace SecureClientPortal.Backend.Tests;

public sealed class SecurityHardeningTests
{
    [Fact]
    public void PasswordHasher_UsesSaltedPbkdf2_AndAcceptsLegacyHashesForMigration()
    {
        const string password = "StrongPassword!2026";
        var first = PasswordHasher.Hash(password);
        var second = PasswordHasher.Hash(password);
        var legacy = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(password)));

        Assert.NotEqual(first, second);
        Assert.StartsWith("PBKDF2-SHA512$", first);
        Assert.True(PasswordHasher.Verify(password, first));
        Assert.False(PasswordHasher.Verify("wrong-password", first));
        Assert.True(PasswordHasher.Verify(password, legacy));
        Assert.True(PasswordHasher.NeedsRehash(legacy));
        Assert.False(PasswordHasher.NeedsRehash(first));
    }

    [Fact]
    public async Task LocalStorage_RejectsSpoofedFiles_AndEncryptsAcceptedFilesAtRest()
    {
        var root = Path.Combine(Path.GetTempPath(), $"scp-storage-test-{Guid.NewGuid():N}");
        try
        {
            var storage = new LocalFileStorage(
                Options.Create(new StorageOptions { RootPath = root }),
                new EphemeralDataProtectionProvider(),
                new FileSecurityScanner());

            await using var spoofedStream = new MemoryStream("not a pdf"u8.ToArray());
            var spoofed = new FormFile(spoofedStream, 0, spoofedStream.Length, "file", "invoice.pdf")
            {
                Headers = new HeaderDictionary(),
                ContentType = "application/pdf"
            };
            await Assert.ThrowsAsync<SecureClientPortal.Backend.Application.Common.AppValidationException>(() =>
                storage.SaveAsync(spoofed, "client-1", TestContext.Current.CancellationToken));

            var content = "%PDF-1.7\nconfidential-client-data\n%%EOF"u8.ToArray();
            await using var validStream = new MemoryStream(content);
            var valid = new FormFile(validStream, 0, validStream.Length, "file", "statement.pdf")
            {
                Headers = new HeaderDictionary(),
                ContentType = "application/pdf"
            };
            var stored = await storage.SaveAsync(valid, "client-1", TestContext.Current.CancellationToken);
            var physicalPath = Path.Combine(root, stored.StorageKey.Replace('/', Path.DirectorySeparatorChar));
            var bytesAtRest = await File.ReadAllBytesAsync(physicalPath, TestContext.Current.CancellationToken);

            Assert.DoesNotContain("confidential-client-data", Encoding.UTF8.GetString(bytesAtRest));
            var reopened = await storage.OpenReadAsync(stored.StorageKey, TestContext.Current.CancellationToken);
            Assert.NotNull(reopened);
            using var reader = new StreamReader(reopened!.Stream, Encoding.UTF8);
            Assert.Equal(Encoding.UTF8.GetString(content), await reader.ReadToEndAsync(TestContext.Current.CancellationToken));
            await storage.DeleteAsync(stored.StorageKey, TestContext.Current.CancellationToken);
            Assert.False(File.Exists(physicalPath));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task LocalStorage_EncryptsLegacyPlaintextFileWhenItIsRead()
    {
        var root = Path.Combine(Path.GetTempPath(), $"scp-storage-test-{Guid.NewGuid():N}");
        try
        {
            var storage = new LocalFileStorage(
                Options.Create(new StorageOptions { RootPath = root }),
                new EphemeralDataProtectionProvider(),
                new FileSecurityScanner());
            var clientPath = Path.Combine(root, "client-1");
            Directory.CreateDirectory(clientPath);
            var physicalPath = Path.Combine(clientPath, "legacy.pdf");
            var content = "%PDF-1.7\nlegacy-confidential-data\n%%EOF"u8.ToArray();
            await File.WriteAllBytesAsync(physicalPath, content, TestContext.Current.CancellationToken);

            var reopened = await storage.OpenReadAsync("client-1/legacy.pdf", TestContext.Current.CancellationToken);

            Assert.NotNull(reopened);
            using var reader = new StreamReader(reopened!.Stream, Encoding.UTF8);
            Assert.Equal(Encoding.UTF8.GetString(content), await reader.ReadToEndAsync(TestContext.Current.CancellationToken));
            var bytesAtRest = await File.ReadAllBytesAsync(physicalPath, TestContext.Current.CancellationToken);
            Assert.DoesNotContain("legacy-confidential-data", Encoding.UTF8.GetString(bytesAtRest));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}

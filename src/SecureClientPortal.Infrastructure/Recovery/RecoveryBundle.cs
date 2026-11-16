using System.IO.Compression;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.DependencyInjection;
using SecureClientPortal.Backend.Infrastructure.Modules.Documents.Storage;

namespace SecureClientPortal.Backend.Infrastructure.Recovery;

public record RecoveryFile(string Path, long Bytes, string Sha256);
public record RecoveryManifest(int Version, string Database, DateTime CreatedAtUtc, RecoveryFile[] Files);

public static class RecoveryBundle
{
    private static readonly byte[] Magic = "SCPBKP01"u8.ToArray();
    private const long Limit = 1_099_511_627_776; // 1 TiB; bounded extraction even for hostile archives.
    public static string PrivateDirectory(string parent)
    {
        var path = Path.Combine(Path.GetFullPath(parent), "portal-recovery-" + Guid.NewGuid().ToString("N"));
        if (OperatingSystem.IsWindows())
        {
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(true, false);
            foreach (var sid in new[] { WindowsIdentity.GetCurrent().User!, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null) })
                security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(path).Create(security);
        }
        else Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }
    public static string SafeChild(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || relative.Contains('\\') || relative.Contains(':') || relative.Split('/').Any(x => x is "." or ".." or ""))
            throw new InvalidDataException("Unsafe recovery path.");
        var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (!path.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) throw new InvalidDataException("Recovery path escapes destination.");
        return path;
    }
    private static IEnumerable<string> SourceFiles(string root)
    {
        var pending = new Stack<string>(); pending.Push(Path.GetFullPath(root));
        while (pending.TryPop(out var directory))
        {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Recovery sources cannot contain links.");
            foreach (var file in Directory.EnumerateFiles(directory))
            { if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Recovery sources cannot contain links."); yield return file; }
            foreach (var child in Directory.EnumerateDirectories(directory)) pending.Push(child);
        }
    }
    public static async Task CreateAsync(string output, string database, string databaseFile, string documents, string keys, string[] certificates, string passphrase, string[] certificatePasswords, CancellationToken ct)
    {
        if (passphrase.Length < 20) throw new ArgumentException("Use a recovery passphrase of at least 20 characters.");
        if (certificates.Length == 0 || certificates.Length != certificatePasswords.Length) throw new ArgumentException("Supply every current/previous key-protection certificate and its password through the secret environment.");
        output = Path.GetFullPath(output);
        foreach (var root in new[] { documents, keys })
            if ((output + Path.DirectorySeparatorChar).StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new ArgumentException("Store backups outside the document and key directories.");
        var stage = PrivateDirectory(Path.GetTempPath());
        var partial = output + "." + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            var entries = new List<(string Name, string Source)> { ("Database/database.bak", databaseFile) };
            entries.AddRange(SourceFiles(documents).Select(path => ("Documents/" + Path.GetRelativePath(documents, path).Replace('\\','/'), path)));
            entries.AddRange(SourceFiles(keys).Select(path => ("KeyRing/" + Path.GetRelativePath(keys, path).Replace('\\','/'), path)));
            entries.AddRange(certificates.Select((path, i) => ($"Certificates/{i}.pfx", path)));
            if (entries.Count > 100_000 || !entries.Any(x => x.Name.StartsWith("KeyRing/") && x.Name.EndsWith(".xml"))) throw new InvalidDataException("Missing key ring or too many recovery files.");
            // Copy the keys into the private workspace; validation must never generate keys in the live ring.
            foreach (var entry in entries.Where(x => x.Name.StartsWith("KeyRing/") || x.Name.StartsWith("Certificates/")))
            { var copy = SafeChild(stage, entry.Name); Directory.CreateDirectory(Path.GetDirectoryName(copy)!); File.Copy(entry.Source, copy); }
            ValidateKeysAndDocuments(documents, Path.Combine(stage,"KeyRing"), Path.Combine(stage,"Certificates"), certificatePasswords);
            var records = new List<RecoveryFile>(); long total = 0;
            var zipPath = Path.Combine(stage, "payload.zip");
            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                foreach (var item in entries)
                {
                    ct.ThrowIfCancellationRequested();
                    var before = new FileInfo(item.Source); var length = before.Length; var modified = before.LastWriteTimeUtc;
                    if ((total += length) > Limit) throw new InvalidDataException("Recovery bundle exceeds the configured limit.");
                    using var source = new FileStream(item.Source, FileMode.Open, FileAccess.Read, FileShare.Read);
                    var hash = Convert.ToHexString(await SHA256.HashDataAsync(source, ct)); source.Position = 0;
                    using var target = zip.CreateEntry(item.Name, CompressionLevel.Fastest).Open(); await source.CopyToAsync(target, ct);
                    before.Refresh(); if (before.Length != length || before.LastWriteTimeUtc != modified) throw new IOException("A source file changed during backup. Keep every API/worker stopped.");
                    records.Add(new RecoveryFile(item.Name,length,hash));
                }
                using var manifest = zip.CreateEntry("manifest.json").Open();
                await JsonSerializer.SerializeAsync(manifest, new RecoveryManifest(1,database,DateTime.UtcNow,records.ToArray()), cancellationToken:ct);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            await using (var destination = new FileStream(partial,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            {
                var salt = RandomNumberGenerator.GetBytes(16); await destination.WriteAsync(Magic,ct); await destination.WriteAsync(salt,ct);
                using var protector = new PassphraseProtector(passphrase,salt);
                await using var source = File.OpenRead(zipPath);
                await ProtectedDocumentStream.WriteAsync(source,destination,protector,Limit,ct);
                destination.Flush(true);
            }
            await ExtractAsync(partial,Path.Combine(stage,"verified"),passphrase,certificatePasswords,ct);
            File.Move(partial,output,false);
        }
        finally { if(File.Exists(partial)) File.Delete(partial); Directory.Delete(stage,true); }
    }
    public static async Task<RecoveryManifest> ExtractAsync(string bundle, string destination, string passphrase, string[] certificatePasswords, CancellationToken ct)
    {
        destination = Path.GetFullPath(destination);
        if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("Recovery destination must not exist; existing files are never overwritten.");
        var parent = Path.GetDirectoryName(destination)!; Directory.CreateDirectory(parent);
        var stage = PrivateDirectory(parent);
        try
        {
            var zipPath = Path.Combine(stage,"payload.zip");
            await using (var input = File.OpenRead(bundle))
            {
                var header = new byte[24]; await input.ReadExactlyAsync(header,ct);
                if (!header.AsSpan(0,8).SequenceEqual(Magic)) throw new InvalidDataException("Not a supported encrypted recovery bundle.");
                using var protector = new PassphraseProtector(passphrase,header[8..]);
                await using var decrypted = await ProtectedDocumentStream.OpenAsync(input,protector,ct);
                await using var output = new FileStream(zipPath,FileMode.CreateNew,FileAccess.Write,FileShare.None);
                var buffer=new byte[64*1024]; long size=0; int read;
                while((read=await decrypted.ReadAsync(buffer,ct))>0) { if((size+=read)>Limit) throw new InvalidDataException("Recovery payload exceeds limit."); await output.WriteAsync(buffer.AsMemory(0,read),ct); }
            }
            var root=PrivateDirectory(stage);
            RecoveryManifest manifest;
            using (var zip=ZipFile.OpenRead(zipPath))
            {
                var metadata=zip.GetEntry("manifest.json")??throw new InvalidDataException("Missing recovery manifest.");
                if(metadata.Length>32_000_000 || zip.Entries.Count>100_001)throw new InvalidDataException("Invalid recovery manifest size.");
                using(var source=metadata.Open()) manifest=await JsonSerializer.DeserializeAsync<RecoveryManifest>(source,cancellationToken:ct)??throw new InvalidDataException("Invalid manifest.");
                if(manifest.Version!=1 || manifest.Files.Length>100_000 || manifest.Files.Select(x=>x.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=manifest.Files.Length || zip.Entries.Count!=manifest.Files.Length+1) throw new InvalidDataException("Invalid recovery manifest.");
                if(!manifest.Files.Any(x=>x.Path=="Database/database.bak") || !manifest.Files.Any(x=>x.Path.StartsWith("KeyRing/")&&x.Path.EndsWith(".xml")) || !manifest.Files.Any(x=>x.Path.StartsWith("Certificates/"))) throw new InvalidDataException("Recovery bundle is incomplete.");
                long total=0;
                foreach(var item in manifest.Files)
                {
                    if(item.Bytes<0 || (total+=item.Bytes)>Limit || !new[]{"Database/","Documents/","KeyRing/","Certificates/"}.Any(prefix=>item.Path.StartsWith(prefix,StringComparison.Ordinal))) throw new InvalidDataException("Invalid recovery entry.");
                    var entry=zip.GetEntry(item.Path)??throw new InvalidDataException("Missing recovery entry.");
                    if(entry.Length!=item.Bytes)throw new InvalidDataException("Recovery entry size mismatch.");
                    var path=SafeChild(root,item.Path); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    using(var source=entry.Open()) await using(var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None)) await CopyBoundedAsync(source,output,item.Bytes,ct);
                    await using var check=File.OpenRead(path);
                    if(Convert.ToHexString(await SHA256.HashDataAsync(check,ct))!=item.Sha256)throw new InvalidDataException("Recovery checksum mismatch.");
                }
            }
            Directory.CreateDirectory(Path.Combine(root,"Documents"));
            ValidateKeysAndDocuments(Path.Combine(root,"Documents"),Path.Combine(root,"KeyRing"),Path.Combine(root,"Certificates"),certificatePasswords);
            Directory.Move(root,destination); return manifest;
        }
        finally { Directory.Delete(stage,true); }
    }
    private static async Task CopyBoundedAsync(Stream source,Stream destination,long expected,CancellationToken ct)
    {
        var buffer=new byte[64*1024];long total=0;int read;
        while((read=await source.ReadAsync(buffer,ct))>0) {if((total+=read)>expected)throw new InvalidDataException("Recovery entry exceeds its declared size.");await destination.WriteAsync(buffer.AsMemory(0,read),ct);}
        if(total!=expected)throw new InvalidDataException("Truncated recovery entry.");
    }
    public static void ValidateKeysAndDocuments(string documents,string keys,string certificates,string[] passwords)
    {
        var files=Directory.GetFiles(certificates,"*.pfx").OrderBy(p=>int.Parse(Path.GetFileNameWithoutExtension(p))).ToArray();
        if(files.Length==0 || files.Length!=passwords.Length)throw new InvalidDataException("Supply all certificate passwords in their original order.");
        var loaded=files.Select((p,i)=>new X509Certificate2(p,passwords[i],X509KeyStorageFlags.EphemeralKeySet)).ToArray();
        try
        {
            if(loaded.Any(x=>!x.HasPrivateKey))throw new CryptographicException("A recovery certificate has no private key.");
            using var services=new ServiceCollection().AddDataProtectionServices(keys,loaded);
            var provider=services.GetRequiredService<IDataProtectionProvider>();
            var protector=provider.CreateProtector("SecureClientPortal.DocumentStorage.v1");
            foreach(var path in SourceFiles(documents).Where(p=>p.EndsWith(".protected",StringComparison.OrdinalIgnoreCase)))
            {
                using var input=File.OpenRead(path); var marker=new byte[4]; input.ReadExactly(marker); input.Position=0;
                if(marker.AsSpan().SequenceEqual(ProtectedDocumentStream.Marker))
                { using var plain=ProtectedDocumentStream.OpenAsync(input,protector,default).GetAwaiter().GetResult(); plain.CopyTo(Stream.Null); }
                else if(marker.AsSpan().SequenceEqual("SCP1"u8))
                { if(input.Length>100_004_096)throw new InvalidDataException("Oversized legacy document requires offline migration."); var bytes=File.ReadAllBytes(path); var plain=protector.Unprotect(bytes[4..]); CryptographicOperations.ZeroMemory(plain); }
                else throw new CryptographicException("Unknown protected document format.");
            }
        }
        finally {foreach(var cert in loaded)cert.Dispose();}
    }
    private static ServiceProvider AddDataProtectionServices(this IServiceCollection services,string keys,X509Certificate2[] certs)
    {
        services.AddDataProtection().SetApplicationName("SecureClientPortal").PersistKeysToFileSystem(new DirectoryInfo(keys)).DisableAutomaticKeyGeneration().UnprotectKeysWithAnyCertificate(certs);
        var provider=services.BuildServiceProvider();
        try { foreach(var key in provider.GetRequiredService<IKeyManager>().GetAllKeys()) key.CreateEncryptor(); return provider; }
        catch {provider.Dispose();throw;}
    }
    private sealed class PassphraseProtector : IDataProtector, IDisposable
    {
        private readonly byte[] key;
        public PassphraseProtector(string passphrase,byte[] salt) { key=Rfc2898DeriveBytes.Pbkdf2(passphrase,salt,600_000,HashAlgorithmName.SHA256,32); }
        public IDataProtector CreateProtector(string purpose)=>throw new NotSupportedException();
        public byte[] Protect(byte[] plain)
        {
            var output=new byte[28+plain.Length]; RandomNumberGenerator.Fill(output.AsSpan(0,12));
            using var aes=new AesGcm(key,16); aes.Encrypt(output.AsSpan(0,12),plain,output.AsSpan(28),output.AsSpan(12,16),Magic); return output;
        }
        public byte[] Unprotect(byte[] data)
        {
            if(data.Length<28)throw new CryptographicException("Invalid recovery record.");
            var plain=new byte[data.Length-28];using var aes=new AesGcm(key,16);aes.Decrypt(data.AsSpan(0,12),data.AsSpan(28),data.AsSpan(12,16),plain,Magic);return plain;
        }
        public void Dispose()=>CryptographicOperations.ZeroMemory(key);
    }
}

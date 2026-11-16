using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using SecureClientPortal.Backend.Infrastructure.Modules.Documents.Storage;
using SecureClientPortal.Backend.Infrastructure.Recovery;

public static class RecoverySmoke
{
    public static async Task RunAsync(Dictionary<string,List<string>> options,CancellationToken ct)
    {
        string Arg(string key)=>options[key].Single();
        var sql=new RecoverySql(options);var suffix=Guid.NewGuid().ToString("N");
        var source="scp_recovery_source_"+suffix;var target="scp_recovery_target_"+suffix;
        var owned=RecoveryBundle.PrivateDirectory(Path.GetTempPath());
        var localFile=Path.Combine(Path.GetFullPath(Arg("--local-staging")),"recovery-smoke-"+suffix+".bak");
        var serverFile=Arg("--sql-staging").TrimEnd('/','\\')+"/"+Path.GetFileName(localFile);
        bool sourceCreated=false,targetCreated=false;
        try
        {
            for(var retry=0;;retry++) {try {await sql.ExistsAsync(source,ct);break;}catch when(retry<45){await Task.Delay(TimeSpan.FromSeconds(2),ct);}}
            if(await sql.ExistsAsync(source,ct)||await sql.ExistsAsync(target,ct))throw new InvalidOperationException("Recovery test database unexpectedly exists.");
            await sql.ExecuteAsync($"CREATE DATABASE {RecoverySql.Identifier(source)}",ct);sourceCreated=true;
            var data=Path.Combine(owned,"source");Directory.CreateDirectory(data);
            var docs=Path.Combine(data,"documents");var keys=Path.Combine(data,"keys");Directory.CreateDirectory(docs);Directory.CreateDirectory(keys);
            using var rsa=RSA.Create(2048);
            var request=new CertificateRequest("CN=Isolated recovery test",rsa,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
            using var certificate=request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow.AddDays(2));
            var certPassword=Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            var certFile=Path.Combine(data,"certificate.pfx");File.WriteAllBytes(certFile,certificate.Export(X509ContentType.Pfx,certPassword));
            var provider=DataProtectionProvider.Create(new DirectoryInfo(keys),builder=>builder.SetApplicationName("SecureClientPortal").ProtectKeysWithCertificate(certificate));
            var storage=new LocalFileStorage(Options.Create(new StorageOptions {RootPath=docs,KeyRingPath=keys}),provider,new FileSecurityScanner());
            var proof=Encoding.UTF8.GetBytes("%PDF-1.7\nRecovery proof "+suffix+"\n%%EOF");
            var upload=new FormFile(new MemoryStream(proof),0,proof.Length,"file","proof.pdf"){Headers=new HeaderDictionary(),ContentType="application/pdf"};
            var saved=await storage.SaveAsync(upload,Guid.NewGuid().ToString(),ct);
            await sql.ExecuteAsync($"USE {RecoverySql.Identifier(source)}; CREATE TABLE AppDocuments(StorageKey nvarchar(1000)); INSERT AppDocuments VALUES(N'{saved.StorageKey.Replace("'","''")}'); CREATE TABLE AppUserSessions(RevokedAtUtc datetime2 NULL,RevokedReason nvarchar(100));INSERT AppUserSessions VALUES(NULL,NULL);CREATE TABLE AppUserAccessTokens(ConsumedAtUtc datetime2 NULL);INSERT AppUserAccessTokens VALUES(NULL);",ct);
            await sql.BackupAsync(source,serverFile,ct);
            var passphrase=Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));var bundle=Path.Combine(owned,"backup.scpbak");
            await RecoveryBundle.CreateAsync(bundle,source,localFile,docs,keys,[certFile],passphrase,[certPassword],ct);
            // Original source data is deliberately removed; recovery must work from the bundle alone.
            await sql.ExecuteAsync($"DROP DATABASE {RecoverySql.Identifier(source)}",ct);sourceCreated=false;
            Directory.Delete(data,true);File.Delete(localFile);
            var destination=Path.Combine(owned,"restored");
            await RecoveryBundle.ExtractAsync(bundle,destination,passphrase,[certPassword],ct);
            File.Copy(Path.Combine(destination,"Database","database.bak"),localFile,false);
            targetCreated=true;await sql.RestoreAsync(target,serverFile,ct);
            await sql.CheckReferencesAsync(target,Path.Combine(destination,"Documents"),ct);
            using var restoredCert=new X509Certificate2(Path.Combine(destination,"Certificates","0.pfx"),certPassword,X509KeyStorageFlags.EphemeralKeySet);
            var restoredProvider=DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(destination,"KeyRing")),builder=>builder.SetApplicationName("SecureClientPortal").DisableAutomaticKeyGeneration().UnprotectKeysWithAnyCertificate(restoredCert));
            var restoredStorage=new LocalFileStorage(Options.Create(new StorageOptions {RootPath=Path.Combine(destination,"Documents")}),restoredProvider,new FileSecurityScanner());
            var content=await restoredStorage.OpenReadAsync(saved.StorageKey,ct)??throw new InvalidDataException("Missing recovered document.");
            await using(content.Stream) {using var bytes=new MemoryStream();await content.Stream.CopyToAsync(bytes,ct);if(!bytes.ToArray().SequenceEqual(proof))throw new CryptographicException("Recovered document differs from original.");}
            await sql.ExecuteAsync($"USE {RecoverySql.Identifier(target)}; IF EXISTS(SELECT 1 FROM AppUserSessions WHERE RevokedAtUtc IS NULL) THROW 51000,'Restored session was not revoked',1; IF EXISTS(SELECT 1 FROM AppUserAccessTokens WHERE ConsumedAtUtc IS NULL) THROW 51000,'Restored access token was not invalidated',1;",ct);
            Console.WriteLine("Full recovery smoke passed: SQL backup/VERIFYONLY, encrypted bundle, original-source removal, new database restore/CHECKDB, reference checks, document decryption and session/token invalidation.");
        }
        finally
        {
            // Names and paths above are freshly generated test resources, never the working database.
            if(sourceCreated)await sql.ExecuteAsync($"ALTER DATABASE {RecoverySql.Identifier(source)} SET SINGLE_USER WITH ROLLBACK IMMEDIATE;DROP DATABASE {RecoverySql.Identifier(source)}",ct);
            if(targetCreated && await sql.ExistsAsync(target,ct))await sql.ExecuteAsync($"ALTER DATABASE {RecoverySql.Identifier(target)} SET SINGLE_USER WITH ROLLBACK IMMEDIATE;DROP DATABASE {RecoverySql.Identifier(target)}",ct);
            if(File.Exists(localFile))File.Delete(localFile);
            Directory.Delete(owned,true);
        }
    }
}

using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using SecureClientPortal.Backend.Infrastructure.Recovery;

try
{
    var command=args.FirstOrDefault()??throw new ArgumentException("Use backup, verify, restore or smoke.");
    if(command is not ("backup" or "restore" or "verify" or "smoke"))throw new ArgumentException("Unknown recovery command.");
    var options=new Dictionary<string,List<string>>();
    for(var i=1;i<args.Length;i++)
    {
        var key=args[i];if(!key.StartsWith("--"))throw new ArgumentException("Use named recovery options.");
        var value=i+1<args.Length&&!args[i+1].StartsWith("--")?args[++i]:"true";
        if(!options.TryGetValue(key,out var list))options[key]=list=[];list.Add(value);
    }
    foreach(var flag in new[]{"--offline","--trust-server-certificate"})
        if(options.TryGetValue(flag,out var values) && (values.Count!=1 || values[0]!="true"))throw new ArgumentException(flag+" is a flag; omit it or supply it without a value.");
    if(options.Keys.Any(key=>key.Contains("password",StringComparison.OrdinalIgnoreCase)||key.Contains("passphrase",StringComparison.OrdinalIgnoreCase)))throw new ArgumentException("Secrets must come from the environment, never arguments.");
    string Required(string key)=>options.TryGetValue(key,out var values)?values.Single():throw new ArgumentException($"Missing {key}.");
    string[] Values(string key)=>options.TryGetValue(key,out var values)?values.ToArray():[];
    string Secret(string name)=>Environment.GetEnvironmentVariable(name)??throw new ArgumentException($"Set {name} through your secret environment; never pass secrets as command arguments.");
    var ct=CancellationToken.None;
    if(command=="smoke") {await RecoverySmoke.RunAsync(options,ct);return 0;}
    var password=Secret("PORTAL_RECOVERY_PASSPHRASE");
    var certPasswords=JsonSerializer.Deserialize<string[]>(Secret("PORTAL_RECOVERY_CERT_PASSWORDS_JSON"))??[];
    if(command=="verify")
    {
        var stage=RecoveryBundle.PrivateDirectory(Path.GetTempPath());
        try {var manifest=await RecoveryBundle.ExtractAsync(Required("--bundle"),Path.Combine(stage,"verified"),password,certPasswords,ct);Console.WriteLine($"Bundle verified: {manifest.Files.Length} files, checksums, key descriptors and document decryption.");}
        finally {Directory.Delete(stage,true);}return 0;
    }
    if(!options.ContainsKey("--offline"))throw new ArgumentException("Stop all API instances and workers first, then explicitly acknowledge with --offline. Live copy backups are not supported.");
    var sql=new RecoverySql(options);
    var db=Required("--database");RecoverySql.Identifier(db);
    var name="portal-recovery-"+Guid.NewGuid().ToString("N")+".bak";
    var localRoot=Path.GetFullPath(Required("--local-staging"));
    if(!Directory.Exists(localRoot))throw new ArgumentException("Provision the private shared SQL backup directory first.");
    var localFile=Path.Combine(localRoot,name);var serverFile=Required("--sql-staging").TrimEnd('/','\\')+"/"+name;
    string? completion=null;
    try
    {
        if(command=="backup")
        {
            await sql.CheckReferencesAsync(db,Required("--documents"),ct);
            await sql.BackupAsync(db,serverFile,ct);
            if(!File.Exists(localFile))throw new IOException("SQL backup is not visible through --local-staging. Both staging paths must refer to the same shared directory.");
            var output=Required("--output");
            await RecoveryBundle.CreateAsync(output,db,localFile,Required("--documents"),Required("--keys"),Values("--certificate"),password,certPasswords,ct);
            completion="Encrypted recovery bundle completed and verified: "+Path.GetFullPath(output);
        }
        else if(command=="restore")
        {
            if(await sql.ExistsAsync(db,ct))throw new InvalidOperationException("Refusing to overwrite an existing database. Choose a new isolated recovery database.");
            var destination=Path.GetFullPath(Required("--destination"));
            var manifest=await RecoveryBundle.ExtractAsync(Required("--bundle"),destination,password,certPasswords,ct);
            File.Copy(Path.Combine(destination,"Database","database.bak"),localFile,false);
            await sql.RestoreAsync(db,serverFile,ct);
            await sql.CheckReferencesAsync(db,Path.Combine(destination,"Documents"),ct);
            completion="Recovery verified into new database "+db+" and "+destination+". All restored sessions and one-time access tokens are invalidated. Keep the API stopped until configuration and access have been reviewed.";
        }
        else throw new ArgumentException("Unknown recovery command.");
    }
    catch
    {
        try { await sql.RecordRecoveryAsync(db, command, ct, "failed"); } catch { /* Do not hide the original recovery error. */ }
        throw;
    }
    finally {if(File.Exists(localFile))File.Delete(localFile);}
    await sql.RecordRecoveryAsync(db, command, ct);
    Console.WriteLine(completion);
    return 0;
}
catch(Exception ex) {Console.Error.WriteLine("Recovery failed: "+ex.Message);return 1;}

public sealed class RecoverySql
{
    private readonly string connection;
    public RecoverySql(Dictionary<string,List<string>> options)
    {
        string Required(string key)=>options.TryGetValue(key,out var list)?list.Single():throw new ArgumentException("Missing "+key);
        var builder=new SqlConnectionStringBuilder {DataSource=Required("--server"),InitialCatalog="master",Encrypt=true,TrustServerCertificate=options.ContainsKey("--trust-server-certificate"),ConnectTimeout=10};
        if(options.TryGetValue("--user",out var user)) {builder.UserID=user.Single();builder.Password=Environment.GetEnvironmentVariable("SQLCMDPASSWORD")??throw new ArgumentException("Set SQLCMDPASSWORD in the secret environment.");}
        else builder.IntegratedSecurity=true;
        connection=builder.ConnectionString;
    }
    public static string Identifier(string name)
    {if(string.IsNullOrWhiteSpace(name)||name.Length>100||name.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c is not '_' and not '-'))throw new ArgumentException("Use a database name containing only letters, digits, underscores and hyphens (maximum 100).");return "["+name+"]";}
    private static string Literal(string value)=>"N'"+value.Replace("'","''")+"'";
    private async Task<SqlConnection> Open(CancellationToken ct) {var sql=new SqlConnection(connection);try {await sql.OpenAsync(ct);return sql;}catch {await sql.DisposeAsync();throw;}}
    public async Task<bool> ExistsAsync(string db,CancellationToken ct)
    {await using var sql=await Open(ct);await using var cmd=new SqlCommand("SELECT DB_ID(@name)",sql);cmd.Parameters.AddWithValue("@name",db);return await cmd.ExecuteScalarAsync(ct) is not DBNull;}
    public async Task ExecuteAsync(string query,CancellationToken ct)
    {await using var sql=await Open(ct);await using var cmd=new SqlCommand(query,sql){CommandTimeout=1800};await cmd.ExecuteNonQueryAsync(ct);}
    public Task BackupAsync(string db,string file,CancellationToken ct)=>ExecuteAsync($"BACKUP DATABASE {Identifier(db)} TO DISK={Literal(file)} WITH COPY_ONLY,CHECKSUM,COMPRESSION,INIT; RESTORE VERIFYONLY FROM DISK={Literal(file)} WITH CHECKSUM;",ct);
    public async Task RestoreAsync(string db,string file,CancellationToken ct)
    {
        if(await ExistsAsync(db,ct))throw new InvalidOperationException("Target database already exists.");
        await using var sql=await Open(ct);
        string data,log;
        await using(var paths=new SqlCommand("SELECT CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(4000)), CAST(SERVERPROPERTY('InstanceDefaultLogPath') AS nvarchar(4000))",sql))
        await using(var reader=await paths.ExecuteReaderAsync(ct)) {await reader.ReadAsync(ct);data=reader.GetString(0);log=reader.GetString(1);}
        var moves=new List<string>();
        await using(var files=new SqlCommand($"RESTORE VERIFYONLY FROM DISK={Literal(file)} WITH CHECKSUM; RESTORE FILELISTONLY FROM DISK={Literal(file)};",sql){CommandTimeout=1800})
        await using(var reader=await files.ExecuteReaderAsync(ct))
        {
            while(reader.FieldCount==0 && await reader.NextResultAsync(ct)) { }
            var index=0;
            while(await reader.ReadAsync(ct))
            {
                var type=reader["Type"].ToString();if(type is not ("D" or "L"))throw new NotSupportedException("Restore requires a separate plan for FILESTREAM or special data files.");
                var path=(type=="L"?log:data).TrimEnd('/','\\')+"/"+db+"_"+(index++)+(type=="L"?".ldf":".mdf");
                moves.Add($"MOVE {Literal(reader["LogicalName"].ToString()!)} TO {Literal(path)}");
            }
        }
        if(moves.Count==0)throw new InvalidDataException("No SQL backup files found.");
        // No WITH REPLACE and no switching an existing database into single-user mode.
        await ExecuteAsync($"RESTORE DATABASE {Identifier(db)} FROM DISK={Literal(file)} WITH {string.Join(",",moves)},CHECKSUM,RECOVERY; DBCC CHECKDB ({Identifier(db)}) WITH NO_INFOMSGS,ALL_ERRORMSGS;",ct);
        await ExecuteAsync($"USE {Identifier(db)}; IF OBJECT_ID('dbo.AppUserSessions') IS NOT NULL UPDATE dbo.AppUserSessions SET RevokedAtUtc=SYSUTCDATETIME(),RevokedReason='disaster_recovery' WHERE RevokedAtUtc IS NULL; IF OBJECT_ID('dbo.AppUserAccessTokens') IS NOT NULL UPDATE dbo.AppUserAccessTokens SET ConsumedAtUtc=SYSUTCDATETIME() WHERE ConsumedAtUtc IS NULL;",ct);
    }
    public async Task RecordRecoveryAsync(string db, string kind, CancellationToken ct, string status = "succeeded")
    {
        var key = "operations." + kind;
        var json = JsonSerializer.Serialize(new { status, atUtc = DateTime.UtcNow, verified = status == "succeeded" });
        await ExecuteAsync($"USE {Identifier(db)}; IF OBJECT_ID('dbo.AppSystemSettings') IS NOT NULL BEGIN UPDATE dbo.AppSystemSettings SET ValueJson={Literal(json)},UpdatedAtUtc=SYSUTCDATETIME() WHERE [Key]={Literal(key)}; IF @@ROWCOUNT=0 INSERT dbo.AppSystemSettings([Key],ValueJson,UpdatedAtUtc) VALUES({Literal(key)},{Literal(json)},SYSUTCDATETIME()); END", ct);
    }
    public async Task CheckReferencesAsync(string db,string documents,CancellationToken ct)
    {
        foreach(var table in new[]{"AppDocuments","AppDocumentVersions","AppComplianceEvidenceVersions"})
        {
            await using var sql=await Open(ct);
            await using var cmd=new SqlCommand($"USE {Identifier(db)}; IF OBJECT_ID('dbo.{table}') IS NOT NULL SELECT StorageKey FROM dbo.{table} WHERE StorageKey IS NOT NULL",sql);
            await using var reader=await cmd.ExecuteReaderAsync(ct);
            if(reader.FieldCount==0)continue;
            while(await reader.ReadAsync(ct))if(!File.Exists(RecoveryBundle.SafeChild(documents,reader.GetString(0).Replace('\\','/'))))throw new InvalidDataException("Restored database references a document missing from the bundle.");
        }
    }
}

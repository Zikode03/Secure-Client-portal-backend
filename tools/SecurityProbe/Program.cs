using System.Diagnostics;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using SecureClientPortal.Backend.Infrastructure.Modules.Documents.Storage;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using SecureClientPortal.Backend.Auth;
using SecureClientPortal.Backend.Data;
using SecureClientPortal.Backend.Models;
using SecureClientPortal.Backend.Domain.Modules.Documents;
using SecureClientPortal.Backend.Domain.Modules.MonthlyPacks;
using SecureClientPortal.Backend.Infrastructure.EntityFrameworkCore;
using SecureClientPortal.Backend.Infrastructure.Recovery;

// Dedicated real-HTTP security assessment. Never reads application connection settings.
// Only a freshly generated test database may be created/dropped by this executable.
var root = Path.GetFullPath(args.Single());
var master = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PORTAL_SECURITY_TEST_SQL") ?? "Server=.;Integrated Security=true;Encrypt=true;TrustServerCertificate=true") { InitialCatalog = "master" };
var name = "scp_security_test_" + Guid.NewGuid().ToString("N");
var connection = new SqlConnectionStringBuilder(master.ConnectionString) { InitialCatalog = name }.ConnectionString;
var owned = RecoveryBundle.PrivateDirectory(Path.GetTempPath());
var ownerId = Guid.NewGuid(); // Owner exists only inside this disposable assessment database.
var signing = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) + "aA1!";
var checks = new List<object>(); var failures = 0; var created = false; Process? process = null;
var log = new StringBuilder();
async Task Sql(string query) { await using var c = new SqlConnection(master.ConnectionString); await c.OpenAsync(); await using var cmd = new SqlCommand(query, c) { CommandTimeout = 120 }; await cmd.ExecuteNonQueryAsync(); }
PortalDbContext Db() => new(new DbContextOptionsBuilder<PortalDbContext>().UseSqlServer(connection, SqlServerMigrationConfiguration.Portal).Options);
void Check(string label, bool pass, string evidence) { checks.Add(new { test = label, passed = pass, evidence }); Console.WriteLine($"{(pass ? "PASS" : "FAIL")} {label}: {evidence}"); if (!pass) failures++; }
string Token(Guid userId, Guid jti, string? key = null, DateTime? expiry = null, string audience = "SecureClientPortal.Client") => new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken("SecureClientPortal", audience, [new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()), new Claim(JwtRegisteredClaimNames.Jti, jti.ToString()), new Claim("role", "admin"), new Claim("permission", "users.create"), new Claim("client_id", Guid.NewGuid().ToString())], expires: expiry ?? DateTime.UtcNow.AddMinutes(15), signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key ?? signing)), SecurityAlgorithms.HmacSha256)));
HttpClient Client(string url, string? token = null) { var h = new HttpClientHandler { CookieContainer = new CookieContainer(), AllowAutoRedirect = false }; var c = new HttpClient(h) { BaseAddress = new Uri(url), Timeout = TimeSpan.FromSeconds(20) }; if (token != null) c.DefaultRequestHeaders.Authorization = new("Bearer", token); return c; }
async Task Status(HttpClient c, string path, int expected, string label, HttpMethod? method = null, HttpContent? body = null, bool csrf = false, string? origin = null) { using var request = new HttpRequestMessage(method ?? HttpMethod.Get, path) { Content = body }; if (csrf) { var token = await c.GetFromJsonAsync<JsonElement>("/api/auth/csrf"); request.Headers.Add("X-CSRF-Token", token.GetProperty("requestToken").GetString()); } if (origin != null) request.Headers.Add("Origin", origin); using var response = await c.SendAsync(request); Check(label, (int)response.StatusCode == expected, $"HTTP {(int)response.StatusCode}; expected {expected}"); }
try
{
    await Sql($"CREATE DATABASE [{name}]"); created = true;
    var portListener = new TcpListener(IPAddress.Loopback, 0); portListener.Start(); var port = ((IPEndPoint)portListener.LocalEndpoint).Port; portListener.Stop(); var url = $"http://127.0.0.1:{port}";
    var start = new ProcessStartInfo("dotnet") { WorkingDirectory = Path.Combine(root, "src/SecureClientPortal.Api"), RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
    start.ArgumentList.Add(Path.Combine(root, "src/SecureClientPortal.Api/bin/Release/net8.0/SecureClientPortal.Api.dll")); start.ArgumentList.Add("--urls"); start.ArgumentList.Add(url);
    foreach (var item in new Dictionary<string, string> { { "Administration__OwnerUserId", ownerId.ToString() }, { "ASPNETCORE_ENVIRONMENT", "Development" }, { "DOTNET_ENVIRONMENT", "Development" }, { "DB_CONNECTION_STRING", connection }, { "JWT_SIGNING_KEY", signing }, { "Jwt__Issuer", "SecureClientPortal" }, { "Jwt__Audience", "SecureClientPortal.Client" }, { "Storage__RootPath", Path.Combine(owned, "documents") }, { "Storage__KeyRingPath", Path.Combine(owned, "keys") }, { "Storage__Scanner", "baseline" }, { "AccessEmail__Enabled", "false" }, { "NotificationEmail__Enabled", "false" }, { "CipcApi__Enabled", "false" }, { "Logging__LogLevel__Default", "Warning" } }) start.Environment[item.Key] = item.Value;
    process = new Process { StartInfo = start }; process.OutputDataReceived += (_, e) => { lock (log) log.AppendLine(e.Data); }; process.ErrorDataReceived += (_, e) => { lock (log) log.AppendLine(e.Data); }; process.Start(); process.BeginOutputReadLine(); process.BeginErrorReadLine();
    using var anon = Client(url); bool ready = false;
    for (var i = 0; i < 120; i++) { if (process.HasExited) throw new Exception("Isolated API exited during startup. See private diagnostic log."); try { using var r = await anon.GetAsync("/api/auth/csrf"); if (r.IsSuccessStatusCode) { ready = true; break; } } catch (HttpRequestException) { } await Task.Delay(500); }
    if (!ready) throw new Exception("Isolated API did not become ready.");
    var a = Guid.NewGuid(); var b = Guid.NewGuid(); var staffId = Guid.NewGuid(); var uaId = Guid.NewGuid(); var ubId = Guid.NewGuid(); var adminId = Guid.NewGuid(); var ja = Guid.NewGuid(); var jb = Guid.NewGuid(); var js = Guid.NewGuid(); var jad = Guid.NewGuid(); var docA = Guid.NewGuid(); var docB = Guid.NewGuid();
    var docsRoot = Path.Combine(owned, "documents");
    var dp = DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(owned, "keys")), builder => builder.SetApplicationName("SecureClientPortal"));
    var storage = new LocalFileStorage(Options.Create(new StorageOptions { RootPath = docsRoot }), dp, new FileSecurityScanner());
    var proof = Encoding.UTF8.GetBytes("%PDF-1.7\nIsolated security document\n%%EOF");
    var file = new FormFile(new MemoryStream(proof), 0, proof.Length, "file", "proof.pdf") { Headers = new HeaderDictionary(), ContentType = "application/pdf" };
    var storedA = await storage.SaveAsync(file, a.ToString());
    file = new FormFile(new MemoryStream(proof), 0, proof.Length, "file", "proof.pdf") { Headers = new HeaderDictionary(), ContentType = "application/pdf" };
    var storedB = await storage.SaveAsync(file, b.ToString());
    Guid paId = Guid.Empty, pbId = Guid.Empty;
    await using (var db = Db())
    {
        db.Clients.AddRange(SecureClientPortal.Backend.Models.Client.Create(a, "Isolated A", "company", "A", "a@example.invalid", ClientStatus.Active), SecureClientPortal.Backend.Models.Client.Create(b, "Isolated B", "company", "B", "b@example.invalid", ClientStatus.Active));
        User Make(Guid id, string email, UserRole role, Guid[] ids) { var u = User.CreateInvited(id, "Security test", email, role, PasswordHasher.Hash(password), JsonSerializer.Serialize(ids.Select(x => x.ToString())), null); u.CompleteSetup("Security test", u.PasswordHash); return u; }
        db.Users.AddRange(Make(uaId, "a@example.invalid", UserRole.Client, [a]), Make(ubId, "b@example.invalid", UserRole.Client, [b]), Make(staffId, "staff@example.invalid", UserRole.Accountant, []), Make(adminId, "admin@example.invalid", UserRole.Admin, []));
        db.ClientAssignments.Add(ClientAssignment.Create(Guid.NewGuid(), staffId, a));
        foreach (var pair in new[] { (uaId, ja), (ubId, jb), (staffId, js), (adminId, jad) }) { var s = UserSession.Start(Guid.NewGuid(), pair.Item1, pair.Item2, DateTime.UtcNow.AddMinutes(30), null, null); s.MarkMfaVerified(); db.UserSessions.Add(s); }
        var pa = MonthlyPack.Create(Guid.NewGuid(), a, 2026, 11); var pb = MonthlyPack.Create(Guid.NewGuid(), b, 2026, 11); db.MonthlyPacks.AddRange(pa, pb); paId = pa.Id; pbId = pb.Id;
        db.Documents.AddRange(Document.CreateUploaded(docA, a, pa.Id, "Only A.pdf", "invoices", null, "application/pdf", proof.Length, storedA.StorageKey, uaId), Document.CreateUploaded(docB, b, pb.Id, "Only B.pdf", "invoices", null, "application/pdf", proof.Length, storedB.StorageKey, ubId)); await db.SaveChangesAsync();
    }
    using var ca = Client(url, Token(uaId, ja)); using var cb = Client(url, Token(ubId, jb)); using var staff = Client(url, Token(staffId, js)); using var admin = Client(url, Token(adminId, jad));
    await Status(anon, "/api/clients", 401, "Anonymous client listing blocked");
    await Status(anon, $"/api/documents/{docA}/download", 401, "Anonymous document download blocked");
    await Status(ca, "/api/auth/me", 200, "Valid client session works despite forged privilege claims");
    await Status(ca, "/api/users", 403, "Client cannot use admin privileges from token");
    await Status(ca, $"/api/clients/{a}", 200, "Own-client positive control");
    await Status(ca, $"/api/clients/{b}", 403, "Cross-client profile blocked");
    await Status(ca, $"/api/documents/{docA}", 200, "Own-document positive control");
    foreach (var suffix in new[] { "", "/download", "/comments" }) await Status(ca, $"/api/documents/{docB}{suffix}", 403, "Cross-client document " + (suffix == "" ? "metadata" : suffix));
    await Status(cb, $"/api/documents/{docA}", 403, "Reverse cross-client document blocked");
    await Status(staff, $"/api/clients/{a}", 200, "Assigned accountant positive control");
    await Status(staff, $"/api/documents/{docB}", 403, "Unassigned accountant document blocked");
    await Status(staff, $"/api/clients/{b}", 403, "Unassigned accountant client blocked");
    var list = await ca.GetFromJsonAsync<JsonElement>("/api/clients"); Check("Client list contains only owned business", list.GetArrayLength() == 1 && list[0].GetProperty("id").GetString() == a.ToString(), "Exactly one owned client");
    await Status(ca, "/api/auth/profile", 403, "Mutation without CSRF blocked", HttpMethod.Put, JsonContent.Create(new { fullName = "Unsafe" }));
    await Status(ca, "/api/auth/profile", 403, "Untrusted origin with valid CSRF blocked", HttpMethod.Put, JsonContent.Create(new { fullName = "Unsafe" }), true, "https://attacker.invalid");
    using (var wrong = Client(url, Token(uaId, ja, Convert.ToBase64String(RandomNumberGenerator.GetBytes(64))))) await Status(wrong, "/api/auth/me", 401, "Forged JWT signature blocked");
    using (var wrong = Client(url, Token(uaId, ja, audience: "attacker"))) await Status(wrong, "/api/auth/me", 401, "Wrong JWT audience blocked");
    using (var expired = Client(url, Token(uaId, ja, expiry: DateTime.UtcNow.AddMinutes(-10)))) await Status(expired, "/api/auth/me", 401, "Expired JWT blocked");
    using (var unknown = Client(url, Token(uaId, Guid.NewGuid()))) await Status(unknown, "/api/auth/me", 401, "Unknown session blocked");
    using (var mismatched = Client(url, Token(uaId, jb))) await Status(mismatched, "/api/auth/me", 401, "Session must belong to JWT subject");
    using (var download = await ca.GetAsync($"/api/documents/{docA}/download")) Check("Own encrypted document downloads correctly", download.IsSuccessStatusCode && (await download.Content.ReadAsByteArrayAsync()).SequenceEqual(proof), "HTTP " + (int)download.StatusCode + "; decrypted bytes compared");
    await Status(ca, "/api/auth/profile", 200, "Valid same-origin CSRF write positive control", HttpMethod.Put, JsonContent.Create(new { fullName = "Verified security test" }), true);
    await Status(ca, $"/api/documents/{docA}/review", 403, "Client cannot approve own documents", HttpMethod.Post, JsonContent.Create(new { decision = "accepted" }), true);
    MultipartFormDataContent Upload(Guid clientId, Guid packId, string filename, byte[] content) { var form = new MultipartFormDataContent(); form.Add(new StringContent(clientId.ToString()), "ClientId"); form.Add(new StringContent(packId.ToString()), "MonthlyPackId"); form.Add(new StringContent("invoices"), "DocumentType"); form.Add(new ByteArrayContent(content), "file", filename); return form; }
    await Status(ca, "/api/documents/upload", 403, "Cross-client file upload blocked", HttpMethod.Post, Upload(b, pbId, "proof.pdf", proof), true);
    await Status(ca, "/api/documents/upload", 400, "Executable disguised as PDF blocked", HttpMethod.Post, Upload(a, paId, "fake.pdf", "MZ executable content"u8.ToArray()), true);
    await Status(ca, "/api/documents/upload", 400, "Executable extension blocked", HttpMethod.Post, Upload(a, paId, "proof.exe", proof), true);
    using (var c = Client(url))
    {
        var token = await c.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login") { Content = JsonContent.Create(new { email = "a@example.invalid", password }) }; request.Headers.Add("X-CSRF-Token", token.GetProperty("requestToken").GetString());
        using var response = await c.SendAsync(request); Check("Real client password login works", response.IsSuccessStatusCode, "HTTP " + (int)response.StatusCode);
        var cookies = response.Headers.TryGetValues("Set-Cookie", out var cookieHeaders) ? cookieHeaders.Where(x => x.StartsWith("scp_access=") || x.StartsWith("scp_refresh=")).ToArray() : [];
        Check("Authentication cookies are HttpOnly and SameSite Strict", cookies.Length == 2 && cookies.All(x => x.Contains("httponly", StringComparison.OrdinalIgnoreCase) && x.Contains("samesite=strict", StringComparison.OrdinalIgnoreCase)), "Cookie flags inspected without recording values");
        await Status(c, "/api/auth/me", 200, "Real cookie session reaches authenticated endpoint");
        await Status(c, "/api/auth/logout", 204, "Logout succeeds with CSRF", HttpMethod.Post, null, true);
        await Status(c, "/api/auth/me", 401, "Logged-out cookie cannot authenticate");
    }
    using (var c = Client(url)) await Status(c, "/api/auth/login", 401, "SQL-like email cannot bypass login", HttpMethod.Post, JsonContent.Create(new { email = "' OR 1=1--@example.invalid", password = "incorrect password value" }), true);
    using (var c = Client(url)) await Status(c, "/api/auth/complete-invite", 401, "Fabricated setup token rejected", HttpMethod.Post, JsonContent.Create(new { email = "a@example.invalid", token = "fabricated-security-test-token", fullName = "Security test", password }), true);
    using (var c = Client(url)) await Status(c, "/api/auth/mfa/verify", 401, "Fabricated MFA challenge rejected", HttpMethod.Post, JsonContent.Create(new { challengeToken = "fabricated-security-test-token", code = "123456" }), true);
    await using (var db = Db()) { var unverified = UserSession.Start(Guid.NewGuid(), staffId, Guid.NewGuid(), DateTime.UtcNow.AddMinutes(30), null, null); db.UserSessions.Add(unverified); await db.SaveChangesAsync(); using var c = Client(url, Token(staffId, unverified.JwtId)); await Status(c, "/api/auth/me", 401, "Staff session requires verified MFA"); }
    await Status(admin,"/api/admin/team-progress",200,"Administrator can view team progress through real SQL aggregation");
    await Status(staff,"/api/admin/team-progress",403,"Accountant cannot access firm-wide team progress");
    await Status(ca,"/api/admin/team-progress",403,"Client cannot access team progress");
    await Status(admin,"/api/admin/operations",200,"Administrator can view live operations");
    await Status(staff,"/api/admin/operations",403,"Accountant cannot inspect admin operational status");
    await Status(ca,"/api/admin/operations",403,"Client cannot inspect admin operational status");
    await Status(admin,$"/api/documents/{docA}/review",403,"Default administrator cannot approve documents",HttpMethod.Post,JsonContent.Create(new{decision="accepted"}),true);
    await Status(admin,$"/api/monthly-packs/{paId}/close",403,"Default administrator cannot close months",HttpMethod.Post,null,true);
    foreach(var section in new[]{"templates/required-documents","templates/monthly-pack","templates/requests","rules/reminders","rules/deadlines"})
    {
        var saved=await admin.GetFromJsonAsync<JsonElement>("/api/admin/firm-management/"+section);
        await Status(admin,"/api/admin/firm-management/"+section,200,"Existing admin configuration saves: "+section,HttpMethod.Put,JsonContent.Create(saved),true);
    }
    await Status(admin,"/api/admin/settings/firm.profile",200,"Firm identity settings save",HttpMethod.Put,JsonContent.Create(new{valueJson="{\"firmName\":\"Isolated firm\",\"email\":\"private@example.invalid\"}"}),true);
    var brand=await ca.GetFromJsonAsync<JsonElement>("/api/firm-branding");Check("Client branding excludes private firm details",brand.GetProperty("firmName").GetString()=="Isolated firm"&&!brand.TryGetProperty("email",out _),"Safe identity fields only");
    await Status(admin,"/api/admin/settings/operations.backup",400,"Backup status cannot be forged through settings",HttpMethod.Put,JsonContent.Create(new{valueJson="{\"status\":\"succeeded\",\"verified\":true}"}),true);
    await Status(admin,$"/api/admin/users/{adminId}/disable",403,"Last administrator cannot disable own access",HttpMethod.Post,JsonContent.Create(new{}),true);
    await Status(admin,$"/api/admin/users/{uaId}/resend-invitation",409,"Invitation resend cannot reset an active account",HttpMethod.Post,JsonContent.Create(new{}),true);
    await using(var db=Db()){var role=await db.RoleDefinitions.SingleAsync(x=>x.Name=="admin");role.UpdateDefinition("Read-only admin","admin","[\"access.admin\",\"settings.read\"]",true);await db.SaveChangesAsync();}
    await Status(admin,"/api/admin/operations",200,"Read-only admin retains operations access");
    await Status(admin,"/api/admin/firm-management/rules/reminders",403,"Read-only admin cannot change reminder rules",HttpMethod.Put,JsonContent.Create(Array.Empty<object>()),true);
    await Status(admin,"/api/admin/users",403,"Read-only admin cannot create users",HttpMethod.Post,JsonContent.Create(new{}),true);
    await using(var db=Db()){var role=await db.RoleDefinitions.SingleAsync(x=>x.Name=="admin");role.UpdateDefinition("Admin","admin",RolePermissions.SerializePermissions(RolePermissions.ForRole("admin")),true);await db.SaveChangesAsync();}
    await using (var db = Db()) { (await db.Users.FindAsync(uaId))!.SetClientIdsJson("[]"); await db.SaveChangesAsync(); }
    await Status(ca, $"/api/documents/{docA}", 403, "Membership removal blocks existing token immediately");
    await using (var db = Db()) { db.ClientAssignments.RemoveRange(db.ClientAssignments.Where(x => x.AccountantUserId == staffId)); await db.SaveChangesAsync(); }
    await Status(staff, $"/api/documents/{docA}", 403, "Assignment removal blocks existing token immediately");
    await using (var db = Db()) { (await db.Users.FindAsync(ubId))!.SetSecurityStatus(SecurityStatus.Disabled); await db.SaveChangesAsync(); }
    await Status(cb, "/api/auth/me", 401, "Disabled account loses existing session");
    await using (var db = Db()) { (await db.UserSessions.SingleAsync(x => x.JwtId == ja)).Revoke("security_test"); await db.SaveChangesAsync(); }
    await Status(ca, "/api/auth/me", 401, "Revoked session blocked immediately");
    // Keep the sole-administrator protection above meaningful: add the owner only now.
    var ownerJti = Guid.NewGuid();
    await using (var db = Db())
    {
        var ownerUser = User.CreateInvited(ownerId, "Security test owner", "owner@example.invalid", UserRole.Admin, PasswordHasher.Hash(password), "[]", null);
        ownerUser.CompleteSetup("Security test owner", ownerUser.PasswordHash);
        db.Users.Add(ownerUser);
        var ownerSession = UserSession.Start(Guid.NewGuid(), ownerId, ownerJti, DateTime.UtcNow.AddMinutes(30), null, null);
        ownerSession.MarkMfaVerified();
        db.UserSessions.Add(ownerSession);
        await db.SaveChangesAsync();
    }
    using var ownerClient = Client(url, Token(ownerId, ownerJti));
    await Status(admin, "/api/roles/accountant", 403, "Ordinary administrator cannot edit role permissions", HttpMethod.Put, JsonContent.Create(new { displayName = "Accountant", scope = "accountant", permissions = new[] { "access.accountant", "clients.read" } }), true);
    await Status(staff, "/api/auth/me", 200, "Denied role edit preserves existing staff session");
    await Status(ownerClient, "/api/roles/accountant", 200, "Configured system owner can restrict accountant role", HttpMethod.Put, JsonContent.Create(new { displayName = "Accountant", scope = "accountant", permissions = new[] { "access.accountant", "clients.read" } }), true);
    await Status(staff, "/api/auth/me", 401, "Role edit revokes existing staff session over HTTP");
    Guid restrictedJti = Guid.NewGuid(); await using (var db = Db()) { var session = UserSession.Start(Guid.NewGuid(), staffId, restrictedJti, DateTime.UtcNow.AddMinutes(30), null, null); session.MarkMfaVerified(); db.UserSessions.Add(session); await db.SaveChangesAsync(); }
    using (var restricted = Client(url, Token(staffId, restrictedJti))) { await Status(restricted, "/api/clients", 200, "Restricted role retains granted read operation"); await Status(restricted, "/api/clients", 403, "Restricted role cannot bypass removed create permission", HttpMethod.Post, JsonContent.Create(new { }), true); }
    await Status(anon, "/api/auth/login", 403, "Login requires CSRF", HttpMethod.Post, JsonContent.Create(new { email = "a@example.invalid", password }));
    using (var burst = Client(url)) { HttpStatusCode last = 0; for (var i = 0; i < 9; i++) { var csrfToken = await burst.GetFromJsonAsync<JsonElement>("/api/auth/csrf"); using var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login") { Content = JsonContent.Create(new { email = "absent@example.invalid", password = "incorrect security test password" }) }; req.Headers.Add("X-CSRF-Token", csrfToken.GetProperty("requestToken").GetString()); using var resp = await burst.SendAsync(req); last = resp.StatusCode; if (last == HttpStatusCode.TooManyRequests) break; } Check("Repeated login attempts are rate limited", last == HttpStatusCode.TooManyRequests, "HTTP " + (int)last); }
    using (var r = await anon.GetAsync("/api/auth/csrf")) { Check("CSRF response is not cacheable", r.Headers.CacheControl?.NoStore == true, "Cache-Control no-store"); Check("MIME sniffing disabled", r.Headers.TryGetValues("X-Content-Type-Options", out var v) && v.Contains("nosniff"), "X-Content-Type-Options"); }
}
catch (Exception ex) { Check("Assessment completed", false, ex.Message); }
finally
{
    if (process is not null) { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); } process.Dispose(); }
    SqlConnection.ClearAllPools();
    if (created) await Sql($"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;DROP DATABASE [{name}]");
    Directory.CreateDirectory(Path.Combine(root, "TestResults")); var report = Path.Combine(root, "TestResults", "security-http-assessment.json"); await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new { assessedAtUtc = DateTime.UtcNow, environment = "isolated-development-http", checks, failures }, new JsonSerializerOptions { WriteIndented = true }));
    if (failures > 0) { var diagnostic = Path.Combine(root, "security-http-assessment.log"); await File.WriteAllTextAsync(diagnostic, log.ToString().Replace(password, "[redacted]").Replace(signing, "[redacted]")); }
    Directory.Delete(owned, true);
}
Console.WriteLine($"Security HTTP assessment: {checks.Count} checks, {failures} failures. Temporary database/storage removed."); return failures == 0 ? 0 : 1;

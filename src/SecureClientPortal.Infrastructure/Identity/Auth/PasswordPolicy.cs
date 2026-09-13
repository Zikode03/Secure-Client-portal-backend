using System.Security.Cryptography;
using System.Text;
using SecureClientPortal.Backend.Application.Common;
namespace SecureClientPortal.Backend.Auth;

public sealed class PasswordPolicy(HttpClient http)
{
    public async Task ValidateAsync(string password, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(password) || password.EnumerateRunes().Count() < 15 || password.Length > 1024)
            throw new AppValidationException("Use a password of at least 15 characters (maximum 1024).");
        // Only a five-character SHA-1 prefix leaves this server; never the password/full hash.
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(password)));
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.pwnedpasswords.com/range/" + hash[..5]);
        request.Headers.Add("Add-Padding", "true");
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode(); // Fail closed if checking is unavailable.
        var body = await response.Content.ReadAsStringAsync(ct);
        var validLines = 0;
        foreach (var line in body.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.Trim().Split(':');
            if (fields.Length != 2 || fields[0].Length != 35 || !fields[0].All(Uri.IsHexDigit) || !long.TryParse(fields[1], out var count) || count < 0)
                throw new HttpRequestException("Password screening returned an invalid response.");
            validLines++;
            if (count > 0 && string.Equals(fields[0], hash[5..], StringComparison.OrdinalIgnoreCase))
                throw new AppValidationException("This password appears in known data breaches. Choose a different passphrase.");
        }
        if (validLines == 0) throw new HttpRequestException("Password screening is unavailable.");
    }
}

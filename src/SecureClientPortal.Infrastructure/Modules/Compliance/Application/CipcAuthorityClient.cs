using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SecureClientPortal.Backend.Application.Modules.Compliance;

namespace SecureClientPortal.Backend.Infrastructure.Modules.Compliance.Application;

public sealed class CipcApiOptions
{
    public const string Section = "CipcApi";
    public bool Enabled { get; set; }
    public string TokenUrl { get; set; } = "";
    public string ApiBaseUrl { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string Scope { get; set; } = "";
    public string ClientAuthenticationMethod { get; set; } = "body";
    public Dictionary<string, CipcCheckOptions> Checks { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class CipcCheckOptions
{
    public string EndpointTemplate { get; set; } = "";
    public string OutcomeProperty { get; set; } = "";
    public string EvidenceProperty { get; set; } = "";
    public string[] PassValues { get; set; } = [];
    public string[] FailValues { get; set; } = [];
    public int ReviewDays { get; set; } = 30;
}

public sealed class CipcAuthorityClient(HttpClient http, IOptions<CipcApiOptions> options) : ICipcAuthorityClient
{
    private const int MaxEvidenceLength = 500;
    private readonly CipcApiOptions config = options.Value;

    public bool IsConfigured(string checkCode)
    {
        return !string.IsNullOrWhiteSpace(checkCode)
            && config.Enabled
            && Uri.TryCreate(config.TokenUrl, UriKind.Absolute, out _)
            && Uri.TryCreate(config.ApiBaseUrl, UriKind.Absolute, out _)
            && !string.IsNullOrWhiteSpace(config.ClientId)
            && !string.IsNullOrWhiteSpace(config.ClientSecret)
            && config.Checks.TryGetValue(checkCode, out var check)
            && !string.IsNullOrWhiteSpace(check.EndpointTemplate)
            && !string.IsNullOrWhiteSpace(check.OutcomeProperty)
            && (check.PassValues?.Length > 0 || check.FailValues?.Length > 0);
    }

    public async Task<AuthorityVerificationResult> VerifyAsync(string checkCode, string registrationNumber, CancellationToken ct)
    {
        if (!IsConfigured(checkCode)) return Failed("CIPC integration is not configured for this check.");
        if (string.IsNullOrWhiteSpace(registrationNumber)) return Failed("A company registration number is required before running a CIPC verification.");

        var check = config.Checks[checkCode];
        try
        {
            var token = await GetTokenAsync(ct);
            if (string.IsNullOrWhiteSpace(token)) return Failed("CIPC authorisation did not return an access token.");

            var relative = check.EndpointTemplate.Replace("{registrationNumber}", Uri.EscapeDataString(registrationNumber.Trim()), StringComparison.Ordinal);
            var endpoint = Uri.TryCreate(relative, UriKind.Absolute, out var absolute)
                ? absolute
                : new Uri(new Uri(config.ApiBaseUrl.TrimEnd('/') + "/"), relative.TrimStart('/'));

            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
                return Failed($"CIPC verification is unavailable (HTTP {(int)response.StatusCode}). No compliance result was saved.");

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (!TryGetPath(document.RootElement, check.OutcomeProperty, out var outcomeElement))
                return Failed("CIPC returned a response that does not match the configured outcome mapping. No result was saved.");

            var rawOutcome = Value(outcomeElement);
            var outcome = Match(rawOutcome, check.PassValues ?? []) ? "pass" : Match(rawOutcome, check.FailValues ?? []) ? "fail" : null;
            if (outcome is null)
                return Failed($"CIPC returned an unmapped status '{Truncate(rawOutcome, 120)}'. No result was saved.");

            var evidence = "CIPC APIVerse";
            if (!string.IsNullOrWhiteSpace(check.EvidenceProperty) && TryGetPath(document.RootElement, check.EvidenceProperty, out var evidenceElement))
            {
                var mapped = Value(evidenceElement);
                if (!string.IsNullOrWhiteSpace(mapped)) evidence = $"CIPC APIVerse · {mapped}";
            }
            evidence = Truncate(evidence, MaxEvidenceLength);

            var checkedAt = DateTime.UtcNow;
            var reviewDays = Math.Clamp(check.ReviewDays, 1, 365);
            return new(true, outcome, evidence, checkedAt, checkedAt.AddDays(reviewDays));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Failed("CIPC verification timed out. No compliance result was saved.");
        }
        catch (HttpRequestException)
        {
            return Failed("CIPC verification could not be reached. No compliance result was saved.");
        }
        catch (JsonException)
        {
            return Failed("CIPC returned an unreadable response. No compliance result was saved.");
        }
    }

    private async Task<string> GetTokenAsync(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, config.TokenUrl);
        var fields = new Dictionary<string, string> { ["grant_type"] = "client_credentials" };
        if (!string.IsNullOrWhiteSpace(config.Scope)) fields["scope"] = config.Scope;

        if (string.Equals(config.ClientAuthenticationMethod, "basic", StringComparison.OrdinalIgnoreCase))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{config.ClientId}:{config.ClientSecret}")));
        }
        else
        {
            fields["client_id"] = config.ClientId;
            fields["client_secret"] = config.ClientSecret;
        }

        request.Content = new FormUrlEncodedContent(fields);
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return "";
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return document.RootElement.TryGetProperty("access_token", out var token) ? token.GetString() ?? "" : "";
    }

    private static bool TryGetPath(JsonElement root, string path, out JsonElement value)
    {
        value = root;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(segment, out value)) return false;
        }
        return true;
    }

    private static string Value(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? "",
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Number => value.GetRawText(),
        _ => value.GetRawText()
    };

    private static bool Match(string value, IEnumerable<string> candidates) =>
        candidates.Any(candidate => string.Equals(candidate?.Trim(), value.Trim(), StringComparison.OrdinalIgnoreCase));

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private static AuthorityVerificationResult Failed(string message) =>
        new(false, "unknown", "", DateTime.UtcNow, DateTime.UtcNow, message);
}

namespace SecureClientPortal.Backend.Models;

public static class ComplianceAlertPolicy
{
    public static string? ComputeAlertLevel(ComplianceItem item, DateTime now)
    {
        if (item.Status == "expired" || item.IsExpiredAt(now))
        {
            return "critical";
        }

        if (item.Status == "rejected")
        {
            return "high";
        }

        if (item.ExpiryDateUtc is DateTime expiry)
        {
            var daysUntilExpiry = (expiry.Date - now.Date).TotalDays;
            if (daysUntilExpiry <= 7)
            {
                return item.RiskLevel == "critical" ? "critical" : "high";
            }

            if (daysUntilExpiry <= 30)
            {
                return "medium";
            }
        }

        if (item.Status is "missing" or "pending")
        {
            return item.RiskLevel is "critical" or "high" ? "high" : "medium";
        }

        return null;
    }

    public static string BuildAlertMessage(ComplianceItem item, string alertLevel)
    {
        if (item.Status == "expired")
        {
            return $"{item.Name} is expired and requires immediate attention.";
        }

        if (item.Status == "rejected")
        {
            return $"{item.Name} was rejected and needs remediation.";
        }

        if (item.ExpiryDateUtc is DateTime expiry)
        {
            return $"{item.Name} is {alertLevel} risk and expires on {expiry:yyyy-MM-dd}.";
        }

        return $"{item.Name} is {alertLevel} risk and needs follow-up.";
    }

    public static string GenerateCategoryCode(string name)
    {
        var compact = new string(name.ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
        return compact.Length switch
        {
            0 => "GEN",
            <= 8 => compact,
            _ => compact[..8]
        };
    }
}

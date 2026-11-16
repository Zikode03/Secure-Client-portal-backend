using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SecureClientPortal.Backend.Domain.Modules.Documents;

public record InvoiceScanDetails(string SupplierName, string InvoiceNumber, string InvoiceDate, string Amount)
{
    public static bool TryParse(string json, out InvoiceScanDetails? details)
    {
        details = null;
        if (json.Length > 1000) return false;
        try
        {
            var value = JsonSerializer.Deserialize<InvoiceScanDetails>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (value is null || string.IsNullOrWhiteSpace(value.SupplierName) || value.SupplierName.Length > 160 ||
                value.InvoiceNumber is null || value.InvoiceNumber.Length > 80 ||
                !DateOnly.TryParseExact(value.InvoiceDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ||
                !Regex.IsMatch(value.Amount ?? "", @"^\d+(\.\d{1,2})?$") ||
                !decimal.TryParse(value.Amount, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount) || amount <= 0) return false;
            details = value with { SupplierName = value.SupplierName.Trim(), InvoiceNumber = value.InvoiceNumber.Trim(), Amount = amount.ToString("0.00", CultureInfo.InvariantCulture) };
            return true;
        }
        catch (JsonException) { return false; }
    }
    public int MatchingFields(InvoiceScanDetails other)
    {
        static string Key(string value) => Regex.Replace(value.ToLowerInvariant(), "[^a-z0-9]", "");
        var count = 0;
        if (Key(SupplierName).Length > 0 && Key(SupplierName) == Key(other.SupplierName)) count++;
        if (Key(InvoiceNumber).Length > 0 && Key(InvoiceNumber) == Key(other.InvoiceNumber)) count++;
        if (InvoiceDate == other.InvoiceDate) count++;
        if (Amount == other.Amount) count++;
        return count;
    }
}

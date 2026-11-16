using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Contracts.Modules.Documents;

namespace SecureClientPortal.Backend.Infrastructure.Modules.Documents.Application;

public static class InvoiceReviewRules
{
    public const string Version = "invoice-v2.0";
    public static bool IsInvoice(string category) => category.Contains("invoice", StringComparison.OrdinalIgnoreCase);
    public static readonly string[] FieldNames = ["supplierName", "invoiceNumber", "invoiceDate", "currency", "invoiceTotal", "amountDue", "subtotal", "vatAmount", "adjustments", "lineItems"];
    public static InvoiceReviewValues Empty => new("", "", "", "ZAR", "", "", "", "", "", false);

    public static InvoiceReviewValues Validate(InvoiceReviewValues values)
    {
        var cleaned = values with {
            SupplierName = (values.SupplierName ?? "").Trim(), InvoiceNumber = (values.InvoiceNumber ?? "").Trim(),
            InvoiceDate = (values.InvoiceDate ?? "").Trim(), Currency = (values.Currency ?? "").Trim().ToUpperInvariant(),
            InvoiceTotal = (values.InvoiceTotal ?? "").Trim(), AmountDue = (values.AmountDue ?? "").Trim(),
            Subtotal = (values.Subtotal ?? "").Trim(), VatAmount = (values.VatAmount ?? "").Trim(), Adjustments = (values.Adjustments ?? "").Trim()
        };
        if (cleaned.SupplierName.Length > 160 || cleaned.InvoiceNumber.Length > 80 || cleaned.Currency.Length > 3 || cleaned.InvoiceDate.Length > 10)
            throw new AppValidationException("Invoice details exceed the field limits.");
        if (cleaned.InvoiceDate.Length > 0 && !TryDate(cleaned.InvoiceDate, out _))
            throw new AppValidationException("Enter a valid invoice date.");
        foreach (var amount in new[] {cleaned.InvoiceTotal, cleaned.AmountDue, cleaned.Subtotal, cleaned.VatAmount, cleaned.Adjustments})
            if (amount.Length > 0 && !TryMoney(amount, out _))
                throw new AppValidationException("Enter amounts as numbers with up to two decimal places, without currency symbols or separators.");
        return cleaned;
    }

    public static InvoiceReviewFinding[] Evaluate(InvoiceReviewValues values, bool originalConfirmed,
        IReadOnlyCollection<InvoiceReviewEvidence> evidence, int year, int month, DateOnly today)
    {
        var findings = new List<InvoiceReviewFinding>();
        void Add(string rule, string result, string message, params string[] fields) =>
            findings.Add(new(FindingId(rule, message), rule, result, message, fields));
        foreach (var (field, value, label) in new[] {
            ("supplierName", values.SupplierName, "Supplier"), ("invoiceNumber", values.InvoiceNumber, "Invoice number"),
            ("invoiceDate", values.InvoiceDate, "Invoice date"), ("invoiceTotal", values.InvoiceTotal, "Invoice total")})
        {
            Add("required." + field, string.IsNullOrWhiteSpace(value) ? "unable_to_verify" : "no_issue_detected",
                string.IsNullOrWhiteSpace(value) ? label + " is missing or could not be read. Check the original." : label + " is recorded.", field);
        }
        Add("currency", values.Currency == "ZAR" ? "no_issue_detected" : "unable_to_verify",
            values.Currency == "ZAR" ? "Currency is recorded as ZAR. Confirm it against the original." : "Automatic calculations support ZAR invoices. Review this currency manually.", "currency");
        if (TryDate(values.InvoiceDate, out var date))
        {
            Add("date.future", date > today ? "needs_attention" : "no_issue_detected",
                date > today ? "The invoice date is in the future. Verify the date against the original." : "The invoice date is not in the future.", "invoiceDate");
            Add("date.period", date.Year == year && date.Month == month ? "no_issue_detected" : "needs_attention",
                date.Year == year && date.Month == month ? "Invoice date matches the selected reporting month." : $"Invoice date {date:yyyy-MM-dd} differs from reporting month {year:D4}-{month:D2}. Confirm the allocation.", "invoiceDate");
        }
        else Add("date.period", "unable_to_verify", "The reporting month cannot be checked until the invoice date is known.", "invoiceDate");
        if (TryMoney(values.InvoiceTotal, out var invoiceTotal) && invoiceTotal <= 0)
            Add("total.sign", "needs_attention", "The invoice total is zero or negative. Confirm the document type and amount against the original.", "invoiceTotal");
        var canCalculate = originalConfirmed && values.AllAdjustmentsAccountedFor && values.Currency == "ZAR"
            && TryMoney(values.Subtotal, out _) && TryMoney(values.VatAmount, out _) && TryMoney(values.InvoiceTotal, out _)
            && TryMoney(values.Adjustments, out _);
        if (canCalculate)
        {
            var expected = decimal.Parse(values.Subtotal, CultureInfo.InvariantCulture) + decimal.Parse(values.VatAmount, CultureInfo.InvariantCulture) + decimal.Parse(values.Adjustments, CultureInfo.InvariantCulture);
            var observed = decimal.Parse(values.InvoiceTotal, CultureInfo.InvariantCulture);
            var difference = observed - expected;
            Add("total.consistency", Math.Abs(difference) <= 0.01m ? "no_issue_detected" : "needs_attention",
                Math.Abs(difference) <= 0.01m ? "The confirmed figures reconcile within R0.01." : $"Possible total mismatch. Subtotal plus VAT and signed adjustments equals R{expected.ToString("0.00", CultureInfo.InvariantCulture)}. The invoice total is R{observed.ToString("0.00", CultureInfo.InvariantCulture)}. Verify the figures and any additional charges.",
                "subtotal", "vatAmount", "adjustments", "invoiceTotal");
        }
        else Add("total.consistency", "unable_to_verify", "To check the total, confirm the original, enter subtotal, VAT and signed adjustments (0 if none), and confirm that all adjustments are accounted for. Amount due is kept separate.", "subtotal", "vatAmount", "adjustments", "invoiceTotal");
        var weakFields = evidence.Where(e => e.Confidence is < 70).Select(e => e.Field).Distinct().ToArray();
        if (weakFields.Length > 0 && !originalConfirmed)
            Add("source.uncertain", "needs_attention", "Some text was difficult to read. Check the highlighted values against the original.", weakFields);
        Add("source.confirmation", originalConfirmed ? "no_issue_detected" : "unable_to_verify",
            originalConfirmed ? "The reviewer recorded a check against the original document." : "An accountant must confirm the details against the original.");
        return findings.ToArray();
    }

    public static bool TryMoney(string value, out decimal result) {
        result = 0;
        return value.Length <= 18 && Regex.IsMatch(value, @"^-?\d+(\.\d{1,2})?$")
            && decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out result)
            && Math.Abs(result) <= 999999999999.99m;
    }
    private static bool TryDate(string value, out DateOnly date) => DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    public static string FindingId(string rule, string value) => rule + "." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..12];
}

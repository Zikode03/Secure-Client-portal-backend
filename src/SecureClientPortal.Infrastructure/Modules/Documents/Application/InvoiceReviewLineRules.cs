using System.Globalization;
using System.Text.RegularExpressions;
using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Contracts.Modules.Documents;

namespace SecureClientPortal.Backend.Infrastructure.Modules.Documents.Application;

public static class InvoiceReviewLineRules
{
    private static bool Number(string? text, out decimal value) {
        value = 0;
        return text is not null && text.Length <= 18 && Regex.IsMatch(text, @"^\d+(\.\d{1,4})?$")
            && decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value) && value <= 1000000000m;
    }
    public static void Validate(InvoiceReviewLineItem[]? lines) {
        if (lines is null) return;
        if (lines.Length > 100) throw new AppValidationException("Use at most 100 invoice line items.");
        foreach (var line in lines) {
            if (line is null || line.Description is null || line.Description.Length > 200)
                throw new AppValidationException("Invoice line descriptions must be at most 200 characters.");
            foreach (var value in new[] {line.Quantity, line.UnitPrice})
                if (value is null || value.Length > 0 && !Number(value, out _))
                    throw new AppValidationException("Line quantities and unit prices must be positive numbers with up to four decimal places.");
            foreach (var value in new[] {line.Discount, line.NetTotal})
                if (value is null || value.Length > 0 && (!InvoiceReviewRules.TryMoney(value, out var amount) || amount < 0))
                    throw new AppValidationException("Line discounts and net totals must be non-negative amounts with up to two decimal places.");
        }
    }
    public static InvoiceReviewFinding[] Evaluate(InvoiceReviewValues values, InvoiceReviewLineItem[]? lines, bool confirmed) {
        if (lines is null) return []; // Legacy reviews did not request line-item checks.
        var findings = new List<InvoiceReviewFinding>();
        void Add(string rule, string result, string message) => findings.Add(new(InvoiceReviewRules.FindingId(rule, message), rule, result, message, ["lineItems"]));
        if (lines is null || lines.Length == 0 || !confirmed || values.Currency != "ZAR") {
            Add("lines.basis", "unable_to_verify", "Line-item checks require all net (before VAT) rows, a discount amount (0 if none), ZAR currency and confirmation against the original. Tax-inclusive or unsupported layouts need manual verification.");
            return findings.ToArray();
        }
        decimal sum = 0; bool complete = true;
        for (var index = 0; index < lines.Length; index++) {
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line.Description) || !Number(line.Quantity, out var quantity) || !Number(line.UnitPrice, out var price)
                || !InvoiceReviewRules.TryMoney(line.Discount, out var discount) || !InvoiceReviewRules.TryMoney(line.NetTotal, out var total)) {
                complete = false;
                Add($"lines.{index}", "unable_to_verify", $"Line {index + 1} has missing or unreadable figures. Check the original."); continue;
            }
            var expected = decimal.Round(quantity * price - discount, 2, MidpointRounding.AwayFromZero);
            sum += total;
            Add($"lines.{index}", Math.Abs(expected - total) <= 0.01m ? "no_issue_detected" : "needs_attention",
                $"Line {index + 1}: quantity × unit price less discount gives R{expected.ToString("0.00", CultureInfo.InvariantCulture)}; recorded net total R{total.ToString("0.00", CultureInfo.InvariantCulture)}. Verify any rounding or adjustments.");
        }
        if (complete && InvoiceReviewRules.TryMoney(values.Subtotal, out var subtotal))
            Add("lines.subtotal", Math.Abs(sum - subtotal) <= 0.01m ? "no_issue_detected" : "needs_attention",
                $"Recorded line net totals add to R{sum.ToString("0.00", CultureInfo.InvariantCulture)}; invoice subtotal R{subtotal.ToString("0.00", CultureInfo.InvariantCulture)}.");
        else Add("lines.subtotal", "unable_to_verify", "The subtotal cannot be reconciled until every line and the subtotal are recorded.");
        return findings.ToArray();
    }
}

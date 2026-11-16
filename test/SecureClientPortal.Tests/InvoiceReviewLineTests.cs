using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Contracts.Modules.Documents;
using SecureClientPortal.Backend.Infrastructure.Modules.Documents.Application;

namespace SecureClientPortal.Backend.Tests;
public sealed class InvoiceReviewLineTests
{
    private static InvoiceReviewValues Values => InvoiceReviewRules.Empty with { Subtotal = "19.00" };
    private static InvoiceReviewLineItem Line => new("Service", "2", "10.00", "1.00", "19.00");
    [Fact] public void ConfirmedNetRowsReconcileUsingDecimalArithmetic() {
        var checks = InvoiceReviewLineRules.Evaluate(Values, [Line], true);
        Assert.All(checks, check => Assert.Equal("no_issue_detected", check.Result));
    }
    [Fact] public void QuantityAndSubtotalMismatchesAreFlaggedSeparately() {
        var checks = InvoiceReviewLineRules.Evaluate(Values, [Line with { NetTotal = "20.00" }], true);
        Assert.Equal(2, checks.Count(check => check.Result == "needs_attention"));
    }
    [Fact] public void MissingDiscountIsUnknownInsteadOfZero() {
        var checks = InvoiceReviewLineRules.Evaluate(Values, [Line with { Discount = "" }], true);
        Assert.All(checks, check => Assert.Equal("unable_to_verify", check.Result));
    }
    [Fact] public void UnconfirmedOrForeignCurrencyRowsCannotPass() {
        Assert.Equal("unable_to_verify", Assert.Single(InvoiceReviewLineRules.Evaluate(Values, [Line], false)).Result);
        Assert.Equal("unable_to_verify", Assert.Single(InvoiceReviewLineRules.Evaluate(Values with { Currency = "USD" }, [Line], true)).Result);
    }
    [Fact] public void FractionalQuantitiesRoundOnlyAtTheLineTotal() {
        var line = Line with { Quantity = "0.125", UnitPrice = "12.3456", Discount = "0", NetTotal = "1.54" };
        var checks = InvoiceReviewLineRules.Evaluate(Values with { Subtotal = "1.54" }, [line], true);
        Assert.All(checks, check => Assert.Equal("no_issue_detected", check.Result));
    }
    [Theory] [InlineData("-1")] [InlineData("1e2")] [InlineData("1.23456")] [InlineData("1000000001")]
    public void InvalidQuantitiesAreRejected(string quantity) => Assert.Throws<AppValidationException>(() => InvoiceReviewLineRules.Validate([Line with { Quantity = quantity }]));
    [Fact] public void LegacyReviewsRemainCompatibleButExplicitEmptyChecksAreUnknown() {
        Assert.Empty(InvoiceReviewLineRules.Evaluate(Values, null, true));
        Assert.Equal("unable_to_verify", Assert.Single(InvoiceReviewLineRules.Evaluate(Values, [], true)).Result);
    }
}

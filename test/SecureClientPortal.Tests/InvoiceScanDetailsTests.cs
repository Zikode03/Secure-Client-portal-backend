using SecureClientPortal.Backend.Domain.Modules.Documents;
namespace SecureClientPortal.Backend.Tests;
public class InvoiceScanDetailsTests
{
    [Fact]
    public void NormalizesConfirmedAmountsAndRejectsInvalidDates()
    {
        Assert.True(InvoiceScanDetails.TryParse("""{"supplierName":"Acme","invoiceNumber":"INV-1","invoiceDate":"2026-11-11","amount":"115"}""", out var details));
        Assert.Equal("115.00", details!.Amount);
        Assert.False(InvoiceScanDetails.TryParse("""{"supplierName":"Acme","invoiceNumber":"INV-1","invoiceDate":"2026-02-31","amount":"115"}""", out _));
        Assert.False(InvoiceScanDetails.TryParse("""{"supplierName":"Acme","invoiceNumber":"INV-1","invoiceDate":"2026-11-11","amount":"-1"}""", out _));
    }
    [Fact]
    public void ComparesMetadataRatherThanTreatingSameAmountAsDefiniteDuplicate()
    {
        var original = new InvoiceScanDetails("Acme Ltd", "INV-1", "2026-11-11", "115.00");
        Assert.Equal(4, original.MatchingFields(new("ACME LTD", "INV1", "2026-11-11", "115.00")));
        Assert.Equal(1, original.MatchingFields(new("Other", "INV-2", "2026-10-01", "115.00")));
    }
}

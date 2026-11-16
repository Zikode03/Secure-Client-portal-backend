using SecureClientPortal.Backend.Infrastructure.Modules.Reports;
using Xunit;

namespace SecureClientPortal.Backend.Tests;

public sealed class ReportFontResolverTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Report_typefaces_resolve_to_bundled_true_type_fonts(bool bold, bool italic)
    {
        var resolver = new ReportFontResolver();
        var face = Assert.IsType<PdfSharp.Fonts.FontResolverInfo>(resolver.ResolveTypeface(ReportFontResolver.Family, bold, italic));
        var bytes = Assert.IsType<byte[]>(resolver.GetFont(face.FaceName));
        Assert.True(bytes.Length > 100_000);
        Assert.Equal(new byte[] {0, 1, 0, 0}, bytes.Take(4).ToArray());
        Assert.Equal(italic, face.MustSimulateItalic);
    }

    [Fact]
    public void Bold_and_regular_use_distinct_embedded_fonts()
    {
        var resolver = new ReportFontResolver();
        var normal = resolver.GetFont(resolver.ResolveTypeface(ReportFontResolver.Family, false, false)!.FaceName)!;
        var bold = resolver.GetFont(resolver.ResolveTypeface(ReportFontResolver.Family, true, false)!.FaceName)!;
        Assert.False(normal.SequenceEqual(bold));
        Assert.Null(resolver.ResolveTypeface("Unconfigured font", false, false));
        Assert.Null(resolver.GetFont("Unconfigured face"));
    }
}

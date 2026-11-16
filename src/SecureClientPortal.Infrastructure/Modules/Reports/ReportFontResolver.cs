using PdfSharp.Fonts;

namespace SecureClientPortal.Backend.Infrastructure.Modules.Reports;

public sealed class ReportFontResolver : IFontResolver
{
    public const string Family = "Noto Sans";
    private const string Regular = "PortalReport.Regular";
    private const string Bold = "PortalReport.Bold";

    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic) =>
        string.Equals(familyName, Family, StringComparison.OrdinalIgnoreCase)
            ? new FontResolverInfo(bold ? Bold : Regular, false, italic)
            : null;

    public byte[]? GetFont(string faceName)
    {
        var file = faceName switch { Regular => "NotoSans-Regular.ttf", Bold => "NotoSans-Bold.ttf", _ => null };
        if (file is null) return null;
        using var source = typeof(ReportFontResolver).Assembly.GetManifestResourceStream($"Portal.ReportFonts.{file}")
            ?? throw new InvalidOperationException($"Bundled report font is missing: {file}");
        using var bytes = new MemoryStream();
        source.CopyTo(bytes);
        return bytes.ToArray();
    }
}

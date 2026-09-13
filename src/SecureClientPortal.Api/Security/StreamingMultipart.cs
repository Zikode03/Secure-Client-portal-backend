using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;
using SecureClientPortal.Backend.Application.Common;
using System.Text;
namespace SecureClientPortal.Backend.Api.Security;

// Disables MVC's form value providers, so multipart files are never model-buffered.
[AttributeUsage(AttributeTargets.Method)]
public sealed class StreamingMultipartAttribute : Attribute, IResourceFilter
{
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        var factories = context.ValueProviderFactories;
        for (var i = factories.Count - 1; i >= 0; i--)
            if (factories[i] is FormValueProviderFactory or FormFileValueProviderFactory or JQueryFormValueProviderFactory)
                factories.RemoveAt(i);
    }
    public void OnResourceExecuted(ResourceExecutedContext context) { }
}
public static class StreamingMultipart
{
    public static async Task<(Dictionary<string, string> Fields, IFormFile File)> ReadAsync(HttpRequest request, CancellationToken ct)
    {
        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var contentType) ||
            !string.Equals(contentType.MediaType.Value, "multipart/form-data", StringComparison.OrdinalIgnoreCase))
            throw new AppValidationException("A multipart upload is required.");
        var boundary = HeaderUtilities.RemoveQuotes(contentType.Boundary).Value;
        if (string.IsNullOrEmpty(boundary) || boundary.Length > 128)
            throw new AppValidationException("Invalid multipart boundary.");
        var reader = new MultipartReader(boundary, request.Body) { HeadersCountLimit = 16, HeadersLengthLimit = 8192, BodyLengthLimit = 100_000_000 };
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadNextSectionAsync(ct) is { } section)
        {
            if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition) ||
                disposition.DispositionType.Value != "form-data")
                throw new AppValidationException("Invalid multipart section.");
            var name = HeaderUtilities.RemoveQuotes(disposition.Name).Value ?? "";
            if (disposition.FileName.HasValue || disposition.FileNameStar.HasValue)
            {
                if (!name.Equals("file", StringComparison.OrdinalIgnoreCase))
                    throw new AppValidationException("The file field must be named File.");
                var fileName = HeaderUtilities.RemoveQuotes(disposition.FileNameStar.HasValue ? disposition.FileNameStar : disposition.FileName).Value ?? "";
                return (fields, new StreamingFormFile(section.Body, fileName, section.ContentType ?? "application/octet-stream"));
            }
            if (fields.Count >= 8 || name.Length > 64 || fields.ContainsKey(name))
                throw new AppValidationException("Too many or duplicate upload fields.");
            var value = new byte[4097];
            var count = await section.Body.ReadAtLeastAsync(value, value.Length, false, ct);
            if (count > 4096) throw new AppValidationException("Upload field is too long.");
            fields.Add(name, Encoding.UTF8.GetString(value, 0, count));
        }
        throw new AppValidationException("Supply metadata fields first and the file last.");
    }
    private sealed class StreamingFormFile(Stream content, string fileName, string contentType) : IFormFile
    {
        // Actual length is counted/enforced by encrypted storage while streaming.
        public long Length => 1;
        public string FileName => Path.GetFileName(fileName.Replace('\\', '/'));
        public string ContentType => contentType;
        public string ContentDisposition => "";
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public string Name => "File";
        public Stream OpenReadStream() => content;
        public void CopyTo(Stream target) => content.CopyTo(target);
        public Task CopyToAsync(Stream target, CancellationToken ct = default) => content.CopyToAsync(target, ct);
    }
}

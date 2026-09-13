using System.Text;
using Microsoft.AspNetCore.Http;
using SecureClientPortal.Backend.Api.Security;
using SecureClientPortal.Backend.Application.Common;
namespace SecureClientPortal.Backend.Tests;
public sealed class StreamingMultipartPhase4Tests
{
    [Fact]
    public async Task MetadataIsParsedBeforeTheFileAndFileRemainsStreaming()
    {
        var payload=new string('x',200_000);
        var body="--boundary\r\nContent-Disposition: form-data; name=\"ClientId\"\r\n\r\nclient-id\r\n--boundary\r\nContent-Disposition: form-data; name=\"File\"; filename=\"document.pdf\"\r\nContent-Type: application/pdf\r\n\r\n"+payload+"\r\n--boundary--\r\n";
        var context=new DefaultHttpContext();
        context.Request.ContentType="multipart/form-data; boundary=boundary";
        context.Request.Body=new MemoryStream(Encoding.ASCII.GetBytes(body));
        var (fields,file)=await StreamingMultipart.ReadAsync(context.Request,TestContext.Current.CancellationToken);
        Assert.Equal("client-id",fields["ClientId"]); Assert.Equal("document.pdf",file.FileName);
        Assert.True(context.Request.Body.Position<10_000);
        using var output=new MemoryStream(); await file.CopyToAsync(output,TestContext.Current.CancellationToken);
        Assert.Equal(payload,Encoding.ASCII.GetString(output.ToArray()));
    }
    [Fact]
    public async Task UnboundedMetadataIsRejected()
    {
        var context=new DefaultHttpContext();
        context.Request.ContentType="multipart/form-data; boundary=boundary";
        context.Request.Body=new MemoryStream(Encoding.ASCII.GetBytes("--boundary\r\nContent-Disposition: form-data; name=\"message\"\r\n\r\n"+new string('x',5000)+"\r\n--boundary--\r\n"));
        await Assert.ThrowsAsync<AppValidationException>(()=>StreamingMultipart.ReadAsync(context.Request,TestContext.Current.CancellationToken));
    }
}

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SecureClientPortal.Backend.Auth;
namespace SecureClientPortal.Backend.Tests;
public class AccessEmailDeliveryTests
{
    [Theory]
    [InlineData(false, "disabled")]
    [InlineData(true, "log")]
    public async Task DisabledOrLegacyLogDeliveryCannotPretendToSendAnInvitation(bool enabled, string mode)
    {
        var sender = new AccessEmailSender(Options.Create(new AccessEmailOptions { Enabled = enabled, DeliveryMode = mode }), NullLogger<AccessEmailSender>.Instance);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendInviteAsync("recipient@test.invalid", "Test recipient", "https://portal.test/invite", DateTime.UtcNow.AddDays(1), TestContext.Current.CancellationToken));
        Assert.Contains("Configure and verify SMTP", error.Message);
    }
}

using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

/// <summary>Sends invite emails. In production an SMTP sender is used; when no
/// SMTP host is configured (local dev) a logging sender records the link
/// instead, so the flow works end-to-end without a mail provider.</summary>
public interface IEmailSender
{
    /// <returns>true if the mail was actually dispatched; false if only logged.</returns>
    Task<bool> SendInviteAsync(string toEmail, string link, CancellationToken ct = default);
}

public sealed record SmtpOptions(string Host, int Port, string? User, string? Pass, string From, string FromName);

public sealed class SmtpEmailSender(SmtpOptions options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task<bool> SendInviteAsync(string toEmail, string link, CancellationToken ct = default)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(options.FromName, options.From));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = "you're invited to the den ✦";
        message.Body = new BodyBuilder
        {
            TextBody =
                $"someone left the door open for you.\n\n" +
                $"redeem your invite here:\n{link}\n\n" +
                $"— roze",
            HtmlBody =
                $"<p>someone left the door open for you.</p>" +
                $"<p><a href=\"{link}\">redeem your invite →</a></p>" +
                $"<p style=\"color:#888\">— roze</p>",
        }.ToMessageBody();

        using var client = new SmtpClient();
        await client.ConnectAsync(options.Host, options.Port, SecureSocketOptions.StartTlsWhenAvailable, ct);
        if (!string.IsNullOrEmpty(options.User))
            await client.AuthenticateAsync(options.User, options.Pass ?? "", ct);
        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);

        logger.LogInformation("Sent invite email to {Email}", toEmail);
        return true;
    }
}

public sealed class LogEmailSender(ILogger<LogEmailSender> logger) : IEmailSender
{
    public Task<bool> SendInviteAsync(string toEmail, string link, CancellationToken ct = default)
    {
        // No SMTP configured — log the link so an admin can copy it from the UI/logs.
        logger.LogInformation("[dev] invite for {Email}: {Link}", toEmail, link);
        return Task.FromResult(false);
    }
}

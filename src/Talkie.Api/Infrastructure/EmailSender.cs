using MailKit.Net.Smtp;
using MimeKit;

namespace Talkie.Api.Infrastructure;

/// <summary>
/// Sends e-mail through SMTP. In development Aspire wires this to MailPit (connection string "mailpit"),
/// so every e-mail can be inspected in the MailPit web UI.
/// </summary>
public sealed class EmailSender(IConfiguration configuration, ILogger<EmailSender> logger)
{
    public async Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
    {
        var endpoint = ParseEndpoint(configuration.GetConnectionString("mailpit"));
        if (endpoint is null)
        {
            logger.LogInformation("No SMTP server configured; skipping e-mail to {To}: {Subject}", to, subject);
            return;
        }

        try
        {
            var message = new MimeMessage();
            message.From.Add(MailboxAddress.Parse(configuration["Mail:From"] ?? "no-reply@talkie.com"));
            message.To.Add(MailboxAddress.Parse(to));
            message.Subject = subject;
            message.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

            using var client = new SmtpClient();
            await client.ConnectAsync(endpoint.Host, endpoint.Port, MailKit.Security.SecureSocketOptions.None, ct);
            await client.SendAsync(message, ct);
            await client.DisconnectAsync(true, ct);
        }
        catch (Exception ex)
        {
            // E-mail is best effort; never fail the request because the mail server is down.
            logger.LogWarning(ex, "Failed to send e-mail to {To}", to);
        }
    }

    /// <summary>Accepts "Endpoint=smtp://host:port" (Aspire MailPit) or a plain "smtp://host:port".</summary>
    internal static Uri? ParseEndpoint(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return null;
        var value = connectionString.Split(';')
            .Select(p => p.Trim())
            .Select(p => p.StartsWith("Endpoint=", StringComparison.OrdinalIgnoreCase) ? p["Endpoint=".Length..] : p)
            .FirstOrDefault(p => p.StartsWith("smtp://", StringComparison.OrdinalIgnoreCase));
        return value is not null && Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : null;
    }
}

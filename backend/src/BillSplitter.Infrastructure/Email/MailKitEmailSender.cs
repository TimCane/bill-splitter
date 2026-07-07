using System.Globalization;
using System.Reflection;
using System.Text;
using BillSplitter.Domain.Abstractions;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace BillSplitter.Infrastructure.Email;

/// <summary>
/// SMTP summary sender. The template is an embedded resource rendered by token
/// replacement - no template engine (docs/07-backend-design.md#infrastructure-project).
/// Send failures are swallowed here: the summary is a courtesy, so a dead relay must
/// never fault the finalize response. Nothing that could identify a person is logged -
/// only the exception type and, where MailKit exposes it, the SMTP status code
/// (docs/10-security-privacy.md#ephemerality-guarantees).
/// </summary>
public sealed class MailKitEmailSender : IEmailSender
{
    private const string TemplateResource = "BillSplitter.Infrastructure.Email.SummaryTemplate.html";

    private readonly string _host;
    private readonly int _port;
    private readonly string? _username;
    private readonly string? _password;
    private readonly string _fromAddress;
    private readonly ILogger<MailKitEmailSender> _logger;
    private readonly string _template;

    public MailKitEmailSender(
        string host,
        int port,
        string? username,
        string? password,
        string fromAddress,
        ILogger<MailKitEmailSender> logger)
    {
        _host = host;
        _port = port;
        _username = username;
        _password = password;
        _fromAddress = fromAddress;
        _logger = logger;
        _template = LoadTemplate();
    }

    public async Task SendSummaryAsync(string toAddress, SummaryEmail summary, CancellationToken ct)
    {
        try
        {
            var message = new MimeMessage();
            message.From.Add(MailboxAddress.Parse(_fromAddress));
            message.To.Add(MailboxAddress.Parse(toAddress));
            message.Subject = "Your split summary";
            message.Body = new BodyBuilder { HtmlBody = Render(summary) }.ToMessageBody();

            using var client = new SmtpClient();
            await client.ConnectAsync(_host, _port, SecureSocketOptions.StartTlsWhenAvailable, ct);
            if (!string.IsNullOrEmpty(_username))
            {
                await client.AuthenticateAsync(_username, _password ?? string.Empty, ct);
            }

            await client.SendAsync(message, ct);
            await client.DisconnectAsync(quit: true, ct);
        }
        catch (SmtpCommandException ex)
        {
            _logger.LogWarning(
                "summary email failed: {Exception} status {Status}", ex.GetType().Name, ex.StatusCode);
        }
        catch (Exception ex)
        {
            // Includes a malformed address (ParseException) and connect/auth faults;
            // the address itself is never part of the log.
            _logger.LogWarning("summary email failed: {Exception}", ex.GetType().Name);
        }
    }

    private string Render(SummaryEmail summary)
    {
        var rows = new StringBuilder();
        foreach (var line in summary.Lines)
        {
            rows.Append("<tr><td style=\"padding: 4px 0;\">")
                .Append(Encode(line.DisplayName))
                .Append("</td><td style=\"padding: 4px 0; text-align: right;\">")
                .Append(FormatMinor(line.TotalMinor, summary.Currency))
                .Append("</td></tr>");
        }

        var unclaimedNote = summary.UnclaimedTotalMinor > 0
            ? $"Unclaimed {FormatMinor(summary.UnclaimedTotalMinor, summary.Currency)} was split between {summary.Lines.Count} people."
            : "Every item was claimed.";

        return _template
            .Replace("{{TOTAL}}", FormatMinor(summary.TotalMinor, summary.Currency))
            .Replace("{{ROWS}}", rows.ToString())
            .Replace("{{UNCLAIMED_NOTE}}", unclaimedNote);
    }

    // Display-only: minor units are the wire and storage form everywhere else; the
    // decimal here never re-enters the domain math (CLAUDE.md).
    private static string FormatMinor(long amountMinor, string currency) =>
        $"{currency} {(amountMinor / 100m).ToString("0.00", CultureInfo.InvariantCulture)}";

    private static string Encode(string value) =>
        value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");

    private static string LoadTemplate()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(TemplateResource)
            ?? throw new InvalidOperationException($"missing embedded template {TemplateResource}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

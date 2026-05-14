using System.Net;
using System.Net.Mail;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Infrastructure.Email;

/// <summary>
/// Implementación alternativa de IEmailService que usa SMTP estándar
/// (Gmail, Office365, Brevo, Mailgun, etc.) en vez de SendGrid.
///
/// Reusa TODAS las plantillas HTML de SendGridEmailService — solo cambia
/// la capa de transporte (SendAsync). Para activarla, en Program.cs del
/// Worker (o donde se registre IEmailService), elegir esta clase cuando
/// la config "Email:Provider" sea "Smtp".
///
/// Variables de entorno requeridas (NO en appsettings.json para evitar
/// commitear credenciales):
///   SMTP__HOST=smtp.gmail.com
///   SMTP__PORT=587
///   SMTP__USER=tu-correo@gmail.com
///   SMTP__PASSWORD=&lt;app-password de 16 caracteres&gt;
///   SMTP__FROMEMAIL=tu-correo@gmail.com   (opcional; default = USER)
///   SMTP__FROMNAME=TalkIA                  (opcional)
///   SMTP__USESSL=true                       (default true; STARTTLS en 587)
///
/// Para Gmail: requiere "App Password"
/// (https://myaccount.google.com/apppasswords). La cuenta debe tener 2FA
/// activado. Límite gratuito de Gmail: ~500 emails/día.
/// </summary>
public class SmtpEmailService(IConfiguration config, ILogger<SmtpEmailService> log)
    : SendGridEmailService(config)
{
    private string SmtpHost => config["Smtp:Host"] ?? throw new InvalidOperationException("Smtp:Host no configurado.");
    private int SmtpPort => int.TryParse(config["Smtp:Port"], out var p) ? p : 587;
    private string SmtpUser => config["Smtp:User"] ?? throw new InvalidOperationException("Smtp:User no configurado.");
    private string SmtpPassword => config["Smtp:Password"] ?? throw new InvalidOperationException("Smtp:Password no configurado.");
    private string SmtpFromEmail => config["Smtp:FromEmail"] ?? config["Smtp:User"]!;
    private string SmtpFromName => config["Smtp:FromName"] ?? "TalkIA";
    private bool SmtpUseSsl => !bool.TryParse(config["Smtp:UseSsl"], out var v) || v;

    protected override async Task<string?> SendAsync(
        string toEmail, string toName, string subject, string htmlContent,
        CancellationToken ct,
        IEnumerable<string>? bccEmails = null,
        string? ccEmail = null)
    {
        // UTF-8 explícito en Subject + Body. Sin esto, MailMessage codifica como
        // ASCII y los acentos/emojis llegan corruptos (PÃ³liza, SofÃ­a, ðŸ˜Š).
        using var msg = new MailMessage
        {
            From = new MailAddress(SmtpFromEmail, SmtpFromName),
            Subject = subject,
            SubjectEncoding = Encoding.UTF8,
            Body = htmlContent,
            BodyEncoding = Encoding.UTF8,
            BodyTransferEncoding = System.Net.Mime.TransferEncoding.Base64,
            IsBodyHtml = true,
            HeadersEncoding = Encoding.UTF8,
        };
        msg.To.Add(new MailAddress(toEmail, toName));

        if (!string.IsNullOrWhiteSpace(ccEmail)
            && !string.Equals(ccEmail, toEmail, StringComparison.OrdinalIgnoreCase))
        {
            try { msg.CC.Add(new MailAddress(ccEmail)); }
            catch (FormatException) { log.LogWarning("[SMTP] CC inválido ignorado: {Cc}", ccEmail); }
        }

        if (bccEmails is not null)
        {
            foreach (var bcc in bccEmails
                .Where(e => !string.IsNullOrWhiteSpace(e)
                            && !string.Equals(e, toEmail, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try { msg.Bcc.Add(new MailAddress(bcc)); }
                catch (FormatException) { log.LogWarning("[SMTP] BCC inválido ignorado: {Bcc}", bcc); }
            }
        }

        using var client = new SmtpClient(SmtpHost, SmtpPort)
        {
            Credentials = new NetworkCredential(SmtpUser, SmtpPassword),
            EnableSsl = SmtpUseSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Timeout = 30_000,
        };

        try
        {
            await client.SendMailAsync(msg, ct);
            log.LogInformation("[SMTP] Enviado a {Email} via {Host}:{Port} (subject={Subject}).",
                toEmail, SmtpHost, SmtpPort, subject);
            // SMTP no devuelve un ID externo correlacionable — el Message-Id del
            // sobre lo conoce solo el receptor. Dejamos null.
            return null;
        }
        catch (SmtpException ex)
        {
            log.LogError(ex, "[SMTP] Falló envío a {Email}: StatusCode={Code} {Message}",
                toEmail, ex.StatusCode, ex.Message);
            throw;
        }
    }
}

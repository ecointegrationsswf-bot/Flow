using Microsoft.Extensions.Configuration;
using SendGrid;
using SendGrid.Helpers.Mail;

namespace AgentFlow.Infrastructure.Email;

public class SendGridEmailService(IConfiguration config) : IEmailService
{
    private SendGridClient GetClient() =>
        new(config["SendGrid:ApiKey"] ?? throw new InvalidOperationException("SendGrid:ApiKey no configurada."));

    private string FromEmail => config["SendGrid:FromEmail"] ?? "noreply@talkia.com";
    private string FromName => config["SendGrid:FromName"] ?? "TalkIA";
    private string AppUrl => config["App:Url"] ?? "https://app.talkia.com";

    public async Task SendWelcomeAdminEmailAsync(string toEmail, string fullName, string password, CancellationToken ct = default)
    {
        var subject = "Bienvenido al Panel Administrativo - TalkIA";
        var html = BuildAdminTemplate(fullName, toEmail, password);
        await SendAsync(toEmail, fullName, subject, html, ct);
    }

    public async Task SendWelcomeTenantEmailAsync(string toEmail, string fullName, string password, string tenantName, CancellationToken ct = default)
    {
        var subject = $"Bienvenido a TalkIA - {tenantName}";
        var html = BuildTenantTemplate(fullName, toEmail, password, tenantName);
        await SendAsync(toEmail, fullName, subject, html, ct);
    }

    public async Task SendTwoFactorCodeAsync(string toEmail, string fullName, string code, CancellationToken ct = default)
    {
        var subject = "Codigo de verificacion - TalkIA";
        var html = Build2FATemplate(fullName, code);
        await SendAsync(toEmail, fullName, subject, html, ct);
    }

    private async Task SendAsync(string toEmail, string toName, string subject, string htmlContent, CancellationToken ct)
    {
        try
        {
            var client = GetClient();
            var from = new EmailAddress(FromEmail, FromName);
            var to = new EmailAddress(toEmail, toName);
            var msg = MailHelper.CreateSingleEmail(from, to, subject, null, htmlContent);
            await client.SendEmailAsync(msg, ct);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error enviando email a {toEmail}: {ex.Message}");
        }
    }

    private string BuildAdminTemplate(string fullName, string email, string password)
    {
        return $"""
        <!DOCTYPE html>
        <html>
        <head><meta charset="utf-8"></head>
        <body style="margin:0;padding:0;background-color:#f4f5f7;font-family:'Segoe UI',Roboto,Arial,sans-serif;">
          <table width="100%" cellpadding="0" cellspacing="0" style="background-color:#f4f5f7;padding:40px 0;">
            <tr><td align="center">
              <table width="560" cellpadding="0" cellspacing="0" style="background-color:#ffffff;border-radius:12px;overflow:hidden;box-shadow:0 2px 8px rgba(0,0,0,0.08);">
                <!-- Header -->
                <tr>
                  <td style="background: linear-gradient(135deg, #1e293b 0%, #334155 100%);padding:32px 40px;text-align:center;">
                    <h1 style="margin:0;color:#f59e0b;font-size:28px;font-weight:700;">TalkIA</h1>
                    <p style="margin:8px 0 0;color:#94a3b8;font-size:14px;">Panel Administrativo</p>
                  </td>
                </tr>
                <!-- Body -->
                <tr>
                  <td style="padding:40px;">
                    <h2 style="margin:0 0 16px;color:#1e293b;font-size:22px;">Hola, {fullName}</h2>
                    <p style="margin:0 0 24px;color:#64748b;font-size:15px;line-height:1.6;">
                      Se ha creado tu cuenta de administrador en la plataforma TalkIA. Con esta cuenta podras gestionar tenants, agentes y toda la configuracion de la plataforma.
                    </p>

                    <!-- Credentials Box -->
                    <table width="100%" cellpadding="0" cellspacing="0" style="background-color:#f8fafc;border:1px solid #e2e8f0;border-radius:8px;margin-bottom:24px;">
                      <tr>
                        <td style="padding:20px;">
                          <p style="margin:0 0 4px;color:#94a3b8;font-size:12px;text-transform:uppercase;letter-spacing:0.5px;">Tus credenciales</p>
                          <table width="100%" cellpadding="0" cellspacing="0">
                            <tr>
                              <td style="padding:8px 0;color:#64748b;font-size:14px;width:100px;">Email:</td>
                              <td style="padding:8px 0;color:#1e293b;font-size:14px;font-weight:600;">{email}</td>
                            </tr>
                            <tr>
                              <td style="padding:8px 0;color:#64748b;font-size:14px;">Contrasena:</td>
                              <td style="padding:8px 0;">
                                <code style="background-color:#fef3c7;color:#92400e;padding:4px 10px;border-radius:4px;font-size:14px;font-weight:600;">{password}</code>
                              </td>
                            </tr>
                          </table>
                        </td>
                      </tr>
                    </table>

                    <!-- CTA Button -->
                    <table width="100%" cellpadding="0" cellspacing="0">
                      <tr><td align="center" style="padding:8px 0 24px;">
                        <a href="{AppUrl}/admin/login" style="display:inline-block;background-color:#f59e0b;color:#1e293b;text-decoration:none;padding:14px 32px;border-radius:8px;font-size:15px;font-weight:600;">
                          Acceder al Panel
                        </a>
                      </td></tr>
                    </table>

                    <p style="margin:0;color:#94a3b8;font-size:13px;line-height:1.5;">
                      Por seguridad, te recomendamos cambiar tu contrasena despues del primer inicio de sesion.
                    </p>
                  </td>
                </tr>
                <!-- Footer -->
                <tr>
                  <td style="background-color:#f8fafc;padding:20px 40px;border-top:1px solid #e2e8f0;text-align:center;">
                    <p style="margin:0;color:#94a3b8;font-size:12px;">&copy; 2026 TalkIA. Todos los derechos reservados.</p>
                  </td>
                </tr>
              </table>
            </td></tr>
          </table>
        </body>
        </html>
        """;
    }

    private string BuildTenantTemplate(string fullName, string email, string password, string tenantName)
    {
        return $"""
        <!DOCTYPE html>
        <html>
        <head><meta charset="utf-8"></head>
        <body style="margin:0;padding:0;background-color:#f4f5f7;font-family:'Segoe UI',Roboto,Arial,sans-serif;">
          <table width="100%" cellpadding="0" cellspacing="0" style="background-color:#f4f5f7;padding:40px 0;">
            <tr><td align="center">
              <table width="560" cellpadding="0" cellspacing="0" style="background-color:#ffffff;border-radius:12px;overflow:hidden;box-shadow:0 2px 8px rgba(0,0,0,0.08);">
                <!-- Header -->
                <tr>
                  <td style="background: linear-gradient(135deg, #1e40af 0%, #3b82f6 100%);padding:32px 40px;text-align:center;">
                    <h1 style="margin:0;color:#ffffff;font-size:28px;font-weight:700;">TalkIA</h1>
                    <p style="margin:8px 0 0;color:#bfdbfe;font-size:14px;">Plataforma de Agentes IA</p>
                  </td>
                </tr>
                <!-- Body -->
                <tr>
                  <td style="padding:40px;">
                    <h2 style="margin:0 0 16px;color:#1e293b;font-size:22px;">Bienvenido, {fullName}</h2>
                    <p style="margin:0 0 24px;color:#64748b;font-size:15px;line-height:1.6;">
                      Tu cuenta ha sido creada en la organizacion <strong style="color:#1e40af;">{tenantName}</strong>. Ya puedes acceder a la plataforma para gestionar agentes de IA, campanas y conversaciones.
                    </p>

                    <!-- Credentials Box -->
                    <table width="100%" cellpadding="0" cellspacing="0" style="background-color:#eff6ff;border:1px solid #bfdbfe;border-radius:8px;margin-bottom:24px;">
                      <tr>
                        <td style="padding:20px;">
                          <p style="margin:0 0 4px;color:#3b82f6;font-size:12px;text-transform:uppercase;letter-spacing:0.5px;">Tus credenciales</p>
                          <table width="100%" cellpadding="0" cellspacing="0">
                            <tr>
                              <td style="padding:8px 0;color:#64748b;font-size:14px;width:120px;">Organizacion:</td>
                              <td style="padding:8px 0;color:#1e293b;font-size:14px;font-weight:600;">{tenantName}</td>
                            </tr>
                            <tr>
                              <td style="padding:8px 0;color:#64748b;font-size:14px;">Email:</td>
                              <td style="padding:8px 0;color:#1e293b;font-size:14px;font-weight:600;">{email}</td>
                            </tr>
                            <tr>
                              <td style="padding:8px 0;color:#64748b;font-size:14px;">Contrasena:</td>
                              <td style="padding:8px 0;">
                                <code style="background-color:#dbeafe;color:#1e40af;padding:4px 10px;border-radius:4px;font-size:14px;font-weight:600;">{password}</code>
                              </td>
                            </tr>
                          </table>
                        </td>
                      </tr>
                    </table>

                    <!-- CTA Button -->
                    <table width="100%" cellpadding="0" cellspacing="0">
                      <tr><td align="center" style="padding:8px 0 24px;">
                        <a href="{AppUrl}/login" style="display:inline-block;background-color:#2563eb;color:#ffffff;text-decoration:none;padding:14px 32px;border-radius:8px;font-size:15px;font-weight:600;">
                          Iniciar Sesion
                        </a>
                      </td></tr>
                    </table>

                    <p style="margin:0;color:#94a3b8;font-size:13px;line-height:1.5;">
                      Por seguridad, te recomendamos cambiar tu contrasena despues del primer inicio de sesion.
                    </p>
                  </td>
                </tr>
                <!-- Footer -->
                <tr>
                  <td style="background-color:#f8fafc;padding:20px 40px;border-top:1px solid #e2e8f0;text-align:center;">
                    <p style="margin:0;color:#94a3b8;font-size:12px;">&copy; 2026 TalkIA. Todos los derechos reservados.</p>
                  </td>
                </tr>
              </table>
            </td></tr>
          </table>
        </body>
        </html>
        """;
    }

    public async Task SendConversationResumeAsync(
        string toEmail,
        string? ccEmail,
        string clientName,
        string clientPhone,
        string? policyNumber,
        List<(string Who, string Text)> messages,
        CancellationToken ct = default)
    {
        var subject = $"Resumen de gestion - {clientName}";
        var html = BuildConversationResumeTemplate(clientName, clientPhone, policyNumber, messages);
        await SendWithCcAsync(toEmail, ccEmail, subject, html, ct);
    }

    private async Task SendWithCcAsync(string toEmail, string? ccEmail, string subject, string htmlContent, CancellationToken ct)
    {
        try
        {
            var client = GetClient();
            var from = new EmailAddress(FromEmail, FromName);
            var to = new EmailAddress(toEmail);
            var msg = MailHelper.CreateSingleEmail(from, to, subject, null, htmlContent);
            if (!string.IsNullOrEmpty(ccEmail))
                msg.AddCc(new EmailAddress(ccEmail));
            await client.SendEmailAsync(msg, ct);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error enviando email a {toEmail}: {ex.Message}");
        }
    }

    private string BuildConversationResumeTemplate(
        string clientName, string clientPhone, string? policyNumber,
        List<(string Who, string Text)> messages)
    {
        var messagesHtml = new System.Text.StringBuilder();
        foreach (var (who, text) in messages)
        {
            var isAgent = who == "Agente";
            var bgColor = isAgent ? "#eff6ff" : "#f8fafc";
            var labelColor = isAgent ? "#2563eb" : "#64748b";
            var label = isAgent ? "Agente IA" : "Cliente";
            messagesHtml.Append($"""
                <tr>
                  <td style="padding:8px 0;border-bottom:1px solid #f1f5f9;">
                    <span style="display:inline-block;font-size:11px;font-weight:600;color:{labelColor};background-color:{bgColor};border-radius:4px;padding:2px 8px;margin-bottom:4px;">{label}</span>
                    <p style="margin:0;color:#374151;font-size:14px;line-height:1.5;">{System.Net.WebUtility.HtmlEncode(text)}</p>
                  </td>
                </tr>
            """);
        }

        var policyRow = policyNumber is not null
            ? $"""<tr><td style="padding:6px 0;color:#64748b;font-size:13px;width:120px;">Poliza:</td><td style="padding:6px 0;color:#1e293b;font-size:13px;font-weight:600;">{System.Net.WebUtility.HtmlEncode(policyNumber)}</td></tr>"""
            : "";

        var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById(
                OperatingSystem.IsWindows() ? "SA Pacific Standard Time" : "America/Panama"));

        return $"""
        <!DOCTYPE html>
        <html>
        <head><meta charset="utf-8"></head>
        <body style="margin:0;padding:0;background-color:#f4f5f7;font-family:'Segoe UI',Roboto,Arial,sans-serif;">
          <table width="100%" cellpadding="0" cellspacing="0" style="background-color:#f4f5f7;padding:40px 0;">
            <tr><td align="center">
              <table width="600" cellpadding="0" cellspacing="0" style="background-color:#ffffff;border-radius:12px;overflow:hidden;box-shadow:0 2px 8px rgba(0,0,0,0.08);">
                <!-- Header -->
                <tr>
                  <td style="background:linear-gradient(135deg,#1e40af 0%,#3b82f6 100%);padding:28px 40px;">
                    <h1 style="margin:0;color:#ffffff;font-size:22px;font-weight:700;">TalkIA</h1>
                    <p style="margin:6px 0 0;color:#bfdbfe;font-size:14px;">Resumen de gestion completada</p>
                  </td>
                </tr>
                <!-- Client info -->
                <tr>
                  <td style="padding:28px 40px 0;">
                    <table width="100%" cellpadding="0" cellspacing="0" style="background-color:#f0fdf4;border:1px solid #bbf7d0;border-radius:8px;">
                      <tr>
                        <td style="padding:16px 20px;">
                          <p style="margin:0 0 10px;color:#15803d;font-size:12px;font-weight:700;text-transform:uppercase;letter-spacing:0.5px;">✅ Gestion exitosa</p>
                          <table width="100%" cellpadding="0" cellspacing="0">
                            <tr>
                              <td style="padding:6px 0;color:#64748b;font-size:13px;width:120px;">Cliente:</td>
                              <td style="padding:6px 0;color:#1e293b;font-size:13px;font-weight:600;">{System.Net.WebUtility.HtmlEncode(clientName)}</td>
                            </tr>
                            <tr>
                              <td style="padding:6px 0;color:#64748b;font-size:13px;">Telefono:</td>
                              <td style="padding:6px 0;color:#1e293b;font-size:13px;font-weight:600;">{System.Net.WebUtility.HtmlEncode(clientPhone)}</td>
                            </tr>
                            {policyRow}
                            <tr>
                              <td style="padding:6px 0;color:#64748b;font-size:13px;">Fecha:</td>
                              <td style="padding:6px 0;color:#1e293b;font-size:13px;">{now:dd/MM/yyyy HH:mm} (Panama)</td>
                            </tr>
                          </table>
                        </td>
                      </tr>
                    </table>
                  </td>
                </tr>
                <!-- Conversation -->
                <tr>
                  <td style="padding:24px 40px 0;">
                    <h2 style="margin:0 0 16px;color:#1e293b;font-size:16px;font-weight:600;">Resumen de la conversacion</h2>
                    <table width="100%" cellpadding="0" cellspacing="0">
                      {messagesHtml}
                    </table>
                  </td>
                </tr>
                <!-- Footer -->
                <tr>
                  <td style="background-color:#f8fafc;padding:20px 40px;border-top:1px solid #e2e8f0;text-align:center;margin-top:28px;">
                    <p style="margin:0;color:#94a3b8;font-size:12px;">&copy; 2026 TalkIA. Todos los derechos reservados.</p>
                  </td>
                </tr>
              </table>
            </td></tr>
          </table>
        </body>
        </html>
        """;
    }

    public async Task SendPasswordResetAsync(string toEmail, string fullName, string resetToken, CancellationToken ct = default)
    {
        var subject = "Restablecer contrasena - TalkIA";
        var html = BuildResetTemplate(fullName, resetToken);
        await SendAsync(toEmail, fullName, subject, html, ct);
    }

    private string BuildResetTemplate(string fullName, string resetToken)
    {
        var resetUrl = $"{AppUrl}/reset-password?token={resetToken}";
        return $"""
        <!DOCTYPE html>
        <html>
        <head><meta charset="utf-8"></head>
        <body style="margin:0;padding:0;background-color:#f4f5f7;font-family:'Segoe UI',Roboto,Arial,sans-serif;">
          <table width="100%" cellpadding="0" cellspacing="0" style="background-color:#f4f5f7;padding:40px 0;">
            <tr><td align="center">
              <table width="520" cellpadding="0" cellspacing="0" style="background-color:#ffffff;border-radius:12px;overflow:hidden;box-shadow:0 2px 8px rgba(0,0,0,0.08);">
                <tr>
                  <td style="background: linear-gradient(135deg, #1e40af 0%, #3b82f6 100%);padding:28px 40px;text-align:center;">
                    <h1 style="margin:0;color:#ffffff;font-size:24px;font-weight:700;">TalkIA</h1>
                  </td>
                </tr>
                <tr>
                  <td style="padding:40px;">
                    <h2 style="margin:0 0 16px;color:#1e293b;font-size:20px;">Restablecer contrasena</h2>
                    <p style="margin:0 0 24px;color:#64748b;font-size:15px;line-height:1.6;">
                      Hola <strong>{fullName}</strong>, recibimos una solicitud para restablecer tu contrasena. Haz clic en el boton de abajo para crear una nueva contrasena.
                    </p>
                    <table width="100%" cellpadding="0" cellspacing="0">
                      <tr><td align="center" style="padding:8px 0 24px;">
                        <a href="{resetUrl}" style="display:inline-block;background-color:#2563eb;color:#ffffff;text-decoration:none;padding:14px 32px;border-radius:8px;font-size:15px;font-weight:600;">
                          Restablecer contrasena
                        </a>
                      </td></tr>
                    </table>
                    <p style="margin:0 0 8px;color:#94a3b8;font-size:13px;">Este enlace expira en <strong>15 minutos</strong>.</p>
                    <p style="margin:0;color:#94a3b8;font-size:13px;">Si no solicitaste restablecer tu contrasena, ignora este mensaje.</p>
                  </td>
                </tr>
                <tr>
                  <td style="background-color:#f8fafc;padding:16px 40px;border-top:1px solid #e2e8f0;text-align:center;">
                    <p style="margin:0;color:#94a3b8;font-size:11px;">&copy; 2026 TalkIA. Todos los derechos reservados.</p>
                  </td>
                </tr>
              </table>
            </td></tr>
          </table>
        </body>
        </html>
        """;
    }

    private string Build2FATemplate(string fullName, string code)
    {
        return $"""
        <!DOCTYPE html>
        <html>
        <head><meta charset="utf-8"></head>
        <body style="margin:0;padding:0;background-color:#f4f5f7;font-family:'Segoe UI',Roboto,Arial,sans-serif;">
          <table width="100%" cellpadding="0" cellspacing="0" style="background-color:#f4f5f7;padding:40px 0;">
            <tr><td align="center">
              <table width="480" cellpadding="0" cellspacing="0" style="background-color:#ffffff;border-radius:12px;overflow:hidden;box-shadow:0 2px 8px rgba(0,0,0,0.08);">
                <tr>
                  <td style="background: linear-gradient(135deg, #0f172a 0%, #1e293b 100%);padding:28px 40px;text-align:center;">
                    <h1 style="margin:0;color:#f59e0b;font-size:24px;font-weight:700;">TalkIA</h1>
                  </td>
                </tr>
                <tr>
                  <td style="padding:40px;text-align:center;">
                    <p style="margin:0 0 8px;color:#64748b;font-size:14px;">Hola, {fullName}</p>
                    <p style="margin:0 0 28px;color:#1e293b;font-size:16px;">Tu codigo de verificacion es:</p>
                    <div style="display:inline-block;background-color:#f8fafc;border:2px solid #e2e8f0;border-radius:12px;padding:20px 40px;margin-bottom:28px;">
                      <span style="font-size:36px;font-weight:700;letter-spacing:8px;color:#1e293b;font-family:'Courier New',monospace;">{code}</span>
                    </div>
                    <p style="margin:0 0 8px;color:#94a3b8;font-size:13px;">Este codigo expira en <strong>5 minutos</strong>.</p>
                    <p style="margin:0;color:#94a3b8;font-size:13px;">Si no solicitaste este codigo, ignora este mensaje.</p>
                  </td>
                </tr>
                <tr>
                  <td style="background-color:#f8fafc;padding:16px 40px;border-top:1px solid #e2e8f0;text-align:center;">
                    <p style="margin:0;color:#94a3b8;font-size:11px;">&copy; 2026 TalkIA. Todos los derechos reservados.</p>
                  </td>
                </tr>
              </table>
            </td></tr>
          </table>
        </body>
        </html>
        """;
    }

    public async Task SendLabelingSummaryAsync(
        string toEmail,
        string fullName,
        string excelUrl,
        IReadOnlyList<(string CampaignName, IReadOnlyDictionary<string, int> CountsByLabel, int Unlabeled)> campaigns,
        CancellationToken ct = default)
    {
        var subject = "TalkIA — Resumen del etiquetado";
        var html = BuildLabelingSummaryTemplate(fullName, excelUrl, campaigns);
        await SendAsync(toEmail, fullName, subject, html, ct);
    }

    private static string BuildLabelingSummaryTemplate(
        string fullName, string excelUrl,
        IReadOnlyList<(string CampaignName, IReadOnlyDictionary<string, int> CountsByLabel, int Unlabeled)> campaigns)
    {
        var sb = new System.Text.StringBuilder();
        var totalConv = 0;
        var totalEtiq = 0;
        foreach (var c in campaigns)
        {
            totalEtiq += c.CountsByLabel.Values.Sum();
            totalConv += c.CountsByLabel.Values.Sum() + c.Unlabeled;
        }

        // Bloque por campaña: nombre + lista de etiquetas con conteo
        var campaignRows = new System.Text.StringBuilder();
        foreach (var c in campaigns)
        {
            campaignRows.Append($"""
            <tr>
              <td style="padding:14px 18px;border-top:1px solid #e2e8f0;">
                <div style="font-weight:600;color:#0f172a;font-size:14px;margin-bottom:6px;">{System.Net.WebUtility.HtmlEncode(c.CampaignName)}</div>
            """);
            if (c.CountsByLabel.Count == 0)
            {
                campaignRows.Append("""
                <div style="font-size:12px;color:#64748b;">Sin conversaciones etiquetadas todavía.</div>
                """);
            }
            else
            {
                foreach (var (label, count) in c.CountsByLabel.OrderByDescending(kv => kv.Value))
                {
                    campaignRows.Append($"""
                    <div style="display:flex;justify-content:space-between;font-size:13px;color:#334155;margin:2px 0;">
                      <span style="color:#475569;">• {System.Net.WebUtility.HtmlEncode(label)}</span>
                      <span style="font-weight:600;color:#0f172a;">{count}</span>
                    </div>
                    """);
                }
                if (c.Unlabeled > 0)
                {
                    campaignRows.Append($"""
                    <div style="display:flex;justify-content:space-between;font-size:12px;color:#94a3b8;margin:2px 0;font-style:italic;">
                      <span>• Sin etiqueta</span>
                      <span>{c.Unlabeled}</span>
                    </div>
                    """);
                }
            }
            campaignRows.Append("</td></tr>");
        }

        sb.Append($"""
        <!DOCTYPE html>
        <html>
        <head><meta charset="utf-8"></head>
        <body style="margin:0;padding:0;background-color:#f4f5f7;font-family:'Segoe UI',Roboto,Arial,sans-serif;">
          <table width="100%" cellpadding="0" cellspacing="0" style="background-color:#f4f5f7;padding:40px 0;">
            <tr><td align="center">
              <table width="600" cellpadding="0" cellspacing="0" style="background-color:#ffffff;border-radius:12px;overflow:hidden;box-shadow:0 2px 8px rgba(0,0,0,0.08);">
                <!-- Header -->
                <tr>
                  <td style="background:linear-gradient(135deg,#1e40af 0%,#3b82f6 100%);padding:32px 40px;text-align:center;">
                    <h1 style="margin:0;color:#ffffff;font-size:26px;font-weight:700;letter-spacing:-0.3px;">Resumen del etiquetado IA</h1>
                    <p style="margin:8px 0 0;color:#dbeafe;font-size:14px;">TalkIA · {DateTime.UtcNow.AddHours(-5):dddd dd 'de' MMMM, yyyy}</p>
                  </td>
                </tr>

                <!-- Saludo -->
                <tr><td style="padding:28px 40px 0;">
                  <h2 style="margin:0 0 8px;color:#0f172a;font-size:18px;">Hola {System.Net.WebUtility.HtmlEncode(fullName)},</h2>
                  <p style="margin:0 0 20px;color:#475569;font-size:14px;line-height:1.6;">
                    Procesamos las conversaciones de tus campañas y asignamos etiquetas con IA.
                    A continuación tienes el detalle por campaña, y al final un botón para descargar
                    el reporte completo en Excel.
                  </p>
                </td></tr>

                <!-- Totales -->
                <tr><td style="padding:0 40px 16px;">
                  <table width="100%" cellpadding="0" cellspacing="0" style="background:#f1f5f9;border-radius:10px;padding:14px 18px;">
                    <tr>
                      <td style="font-size:13px;color:#475569;">Conversaciones etiquetadas</td>
                      <td style="font-size:18px;font-weight:700;color:#1e40af;text-align:right;">{totalEtiq}</td>
                    </tr>
                    <tr>
                      <td style="font-size:12px;color:#64748b;">Total de conversaciones (incluye sin etiqueta)</td>
                      <td style="font-size:13px;color:#64748b;text-align:right;">{totalConv}</td>
                    </tr>
                    <tr>
                      <td style="font-size:12px;color:#64748b;">Campañas en el reporte</td>
                      <td style="font-size:13px;color:#64748b;text-align:right;">{campaigns.Count}</td>
                    </tr>
                  </table>
                </td></tr>

                <!-- Detalle por campaña -->
                <tr><td style="padding:8px 40px 8px;">
                  <h3 style="margin:0 0 4px;font-size:14px;color:#1e293b;text-transform:uppercase;letter-spacing:0.4px;">Detalle por campaña</h3>
                  <table width="100%" cellpadding="0" cellspacing="0" style="border:1px solid #e2e8f0;border-radius:8px;background:#ffffff;">
                    {campaignRows}
                  </table>
                </td></tr>

                <!-- CTA -->
                <tr><td align="center" style="padding:28px 40px 8px;">
                  <a href="{System.Net.WebUtility.HtmlEncode(excelUrl)}"
                     style="display:inline-block;background-color:#1d4ed8;color:#ffffff;text-decoration:none;font-weight:600;font-size:14px;padding:12px 32px;border-radius:8px;">
                    📊 Descargar reporte (Excel)
                  </a>
                  <p style="margin:14px 0 0;font-size:11px;color:#94a3b8;">
                    El archivo está disponible para descarga directa desde Azure.
                  </p>
                </td></tr>

                <!-- Footer -->
                <tr><td style="padding:24px 40px 32px;text-align:center;border-top:1px solid #e2e8f0;margin-top:16px;">
                  <p style="margin:0;font-size:11px;color:#94a3b8;">
                    Este es un mensaje automático de TalkIA. No respondas a este correo.
                  </p>
                </td></tr>
              </table>
            </td></tr>
          </table>
        </body>
        </html>
        """);

        return sb.ToString();
    }
}

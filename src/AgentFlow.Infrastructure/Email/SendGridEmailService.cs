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

    private async Task SendAsync(
        string toEmail, string toName, string subject, string htmlContent,
        CancellationToken ct,
        IEnumerable<string>? bccEmails = null)
    {
        try
        {
            var client = GetClient();
            var from = new EmailAddress(FromEmail, FromName);
            var to = new EmailAddress(toEmail, toName);
            var msg = MailHelper.CreateSingleEmail(from, to, subject, null, htmlContent);

            // BCC silencioso (típicamente a super admins). El destinatario principal
            // no ve estas direcciones — SendGrid las añade en sobres SMTP separados.
            if (bccEmails is not null)
            {
                foreach (var bcc in bccEmails
                    .Where(e => !string.IsNullOrWhiteSpace(e)
                                && !string.Equals(e, toEmail, StringComparison.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    msg.AddBcc(new EmailAddress(bcc));
                }
            }

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
        IEnumerable<string>? bccEmails = null,
        CancellationToken ct = default)
    {
        var subject = "TalkIA — Resumen del etiquetado";
        var html = BuildLabelingSummaryTemplate(fullName, excelUrl, campaigns);
        await SendAsync(toEmail, fullName, subject, html, ct, bccEmails);
    }

    // Paleta de 8 colores distinguibles para etiquetas; el último (gris) reservado a "Sin etiqueta".
    private static readonly string[] LabelPalette =
    {
        "#3b82f6", // blue-500
        "#22c55e", // green-500
        "#f59e0b", // amber-500
        "#a855f7", // purple-500
        "#14b8a6", // teal-500
        "#ec4899", // pink-500
        "#6366f1", // indigo-500
        "#ef4444", // red-500
    };
    private const string UnlabeledColor = "#cbd5e1"; // slate-300

    private static string BuildLabelingSummaryTemplate(
        string fullName, string excelUrl,
        IReadOnlyList<(string CampaignName, IReadOnlyDictionary<string, int> CountsByLabel, int Unlabeled)> campaigns)
    {
        var totalConv = 0;
        var totalEtiq = 0;
        var totalUnlabeled = 0;

        // Agregar global para el chart: etiqueta → conteo total entre todas las campañas
        var globalLabelCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in campaigns)
        {
            foreach (var (label, count) in c.CountsByLabel)
            {
                globalLabelCounts.TryGetValue(label, out var prev);
                globalLabelCounts[label] = prev + count;
                totalEtiq += count;
            }
            totalUnlabeled += c.Unlabeled;
            totalConv += c.CountsByLabel.Values.Sum() + c.Unlabeled;
        }

        // Mapear cada etiqueta a un color estable basado en orden alfabético del set global.
        var orderedLabels = globalLabelCounts.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
        var labelColors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < orderedLabels.Count; i++)
            labelColors[orderedLabels[i]] = LabelPalette[i % LabelPalette.Length];

        var pct = totalConv == 0 ? 0 : (int)Math.Round(totalEtiq * 100.0 / totalConv);

        // ── URL del pie chart vía QuickChart.io ──────────────────────────────
        // QuickChart renderiza Chart.js a PNG. Funciona en todos los clientes
        // de email (incluido Gmail/Outlook que filtran SVG/JS).
        var chartUrl = totalEtiq + totalUnlabeled == 0
            ? null
            : BuildPieChartUrl(globalLabelCounts, totalUnlabeled, labelColors);

        // Bloque de campañas con progress bars
        var campaignBlocks = new System.Text.StringBuilder();
        foreach (var c in campaigns)
        {
            var cTotal = c.CountsByLabel.Values.Sum() + c.Unlabeled;
            var cEtiq = c.CountsByLabel.Values.Sum();
            var cPct = cTotal == 0 ? 0 : (int)Math.Round(cEtiq * 100.0 / cTotal);

            campaignBlocks.Append($"""
            <tr><td style="padding:14px 20px;border-top:1px solid #e2e8f0;">
              <table width="100%" cellpadding="0" cellspacing="0">
                <tr>
                  <td style="font-weight:600;color:#0f172a;font-size:14px;">{System.Net.WebUtility.HtmlEncode(c.CampaignName)}</td>
                  <td style="text-align:right;font-size:12px;color:#64748b;white-space:nowrap;">{cEtiq} / {cTotal} · {cPct}%</td>
                </tr>
              </table>
              <!-- Progress bar -->
              <div style="margin:8px 0 10px;height:6px;background:#e2e8f0;border-radius:3px;overflow:hidden;">
                <div style="height:6px;width:{cPct}%;background:linear-gradient(90deg,#3b82f6,#1d4ed8);border-radius:3px;"></div>
              </div>
            """);

            if (c.CountsByLabel.Count == 0)
            {
                campaignBlocks.Append("""
                <div style="font-size:12px;color:#94a3b8;font-style:italic;">Sin conversaciones etiquetadas todavía.</div>
                """);
            }
            else
            {
                campaignBlocks.Append("<div>");
                foreach (var (label, count) in c.CountsByLabel.OrderByDescending(kv => kv.Value))
                {
                    var color = labelColors.TryGetValue(label, out var lc) ? lc : LabelPalette[0];
                    campaignBlocks.Append($"""
                    <span style="display:inline-block;margin:2px 4px 2px 0;padding:3px 10px;font-size:12px;border-radius:999px;background:{color}22;color:{color};font-weight:600;border:1px solid {color}44;">
                      {System.Net.WebUtility.HtmlEncode(label)} · {count}
                    </span>
                    """);
                }
                if (c.Unlabeled > 0)
                {
                    campaignBlocks.Append($"""
                    <span style="display:inline-block;margin:2px 4px 2px 0;padding:3px 10px;font-size:12px;border-radius:999px;background:#f1f5f9;color:#64748b;font-weight:500;border:1px solid #e2e8f0;">
                      Sin etiqueta · {c.Unlabeled}
                    </span>
                    """);
                }
                campaignBlocks.Append("</div>");
            }
            campaignBlocks.Append("</td></tr>");
        }

        // Leyenda de colores para el chart (visible aún si la imagen no carga)
        var legendItems = new System.Text.StringBuilder();
        foreach (var lbl in orderedLabels)
        {
            var color = labelColors[lbl];
            var count = globalLabelCounts[lbl];
            var lblPct = totalConv == 0 ? 0 : (int)Math.Round(count * 100.0 / totalConv);
            legendItems.Append($"""
            <tr>
              <td width="14" style="padding:4px 0;"><div style="width:10px;height:10px;background:{color};border-radius:2px;"></div></td>
              <td style="padding:4px 8px;font-size:12px;color:#334155;">{System.Net.WebUtility.HtmlEncode(lbl)}</td>
              <td style="padding:4px 0;font-size:12px;color:#0f172a;font-weight:600;text-align:right;white-space:nowrap;">{count} <span style="color:#94a3b8;font-weight:400;">({lblPct}%)</span></td>
            </tr>
            """);
        }
        if (totalUnlabeled > 0)
        {
            var ulPct = totalConv == 0 ? 0 : (int)Math.Round(totalUnlabeled * 100.0 / totalConv);
            legendItems.Append($"""
            <tr>
              <td width="14" style="padding:4px 0;"><div style="width:10px;height:10px;background:{UnlabeledColor};border-radius:2px;"></div></td>
              <td style="padding:4px 8px;font-size:12px;color:#64748b;font-style:italic;">Sin etiqueta</td>
              <td style="padding:4px 0;font-size:12px;color:#64748b;font-weight:500;text-align:right;white-space:nowrap;">{totalUnlabeled} <span style="color:#94a3b8;font-weight:400;">({ulPct}%)</span></td>
            </tr>
            """);
        }

        var chartSection = chartUrl is null ? "" : $"""
        <tr><td style="padding:8px 40px 16px;">
          <h3 style="margin:0 0 12px;font-size:13px;color:#1e293b;text-transform:uppercase;letter-spacing:0.5px;font-weight:700;">Distribución de etiquetas</h3>
          <table width="100%" cellpadding="0" cellspacing="0" style="background:#f8fafc;border:1px solid #e2e8f0;border-radius:10px;padding:16px;">
            <tr>
              <td width="220" valign="middle" align="center">
                <img src="{System.Net.WebUtility.HtmlEncode(chartUrl)}" alt="Distribución de etiquetas" width="200" style="display:block;border:0;outline:none;text-decoration:none;max-width:100%;height:auto;" />
              </td>
              <td valign="middle" style="padding-left:16px;">
                <table cellpadding="0" cellspacing="0" width="100%">{legendItems}</table>
              </td>
            </tr>
          </table>
        </td></tr>
        """;

        var html = $$"""
        <!DOCTYPE html>
        <html>
        <head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"></head>
        <body style="margin:0;padding:0;background-color:#f4f5f7;font-family:'Segoe UI',Roboto,Arial,sans-serif;">
          <table width="100%" cellpadding="0" cellspacing="0" style="background-color:#f4f5f7;padding:40px 0;">
            <tr><td align="center">
              <table width="640" cellpadding="0" cellspacing="0" style="background-color:#ffffff;border-radius:14px;overflow:hidden;box-shadow:0 4px 16px rgba(15,23,42,0.08);">
                <!-- Header con gradient -->
                <tr><td style="background:linear-gradient(135deg,#1e3a8a 0%,#3b82f6 60%,#06b6d4 100%);padding:36px 40px;text-align:center;">
                  <div style="display:inline-block;background:rgba(255,255,255,0.15);padding:6px 14px;border-radius:999px;margin-bottom:14px;">
                    <span style="color:#ffffff;font-size:11px;font-weight:600;letter-spacing:1px;text-transform:uppercase;">TalkIA · Resumen IA</span>
                  </div>
                  <h1 style="margin:0;color:#ffffff;font-size:28px;font-weight:700;letter-spacing:-0.4px;">Resumen del etiquetado</h1>
                  <p style="margin:8px 0 0;color:#dbeafe;font-size:14px;">{{DateTime.UtcNow.AddHours(-5):dddd dd 'de' MMMM 'de' yyyy}}</p>
                </td></tr>

                <!-- Saludo -->
                <tr><td style="padding:28px 40px 0;">
                  <h2 style="margin:0 0 8px;color:#0f172a;font-size:19px;font-weight:600;">Hola {{System.Net.WebUtility.HtmlEncode(fullName)}} 👋</h2>
                  <p style="margin:0 0 8px;color:#475569;font-size:14px;line-height:1.6;">
                    Aquí tienes el resumen del análisis automático de tus conversaciones por la IA.
                  </p>
                </td></tr>

                <!-- KPI Cards -->
                <tr><td style="padding:18px 40px 12px;">
                  <table width="100%" cellpadding="0" cellspacing="0">
                    <tr>
                      <td width="33%" style="padding:0 5px 0 0;">
                        <div style="background:linear-gradient(135deg,#dbeafe 0%,#eff6ff 100%);border:1px solid #bfdbfe;border-radius:10px;padding:14px 12px;text-align:center;">
                          <div style="font-size:11px;color:#1e40af;text-transform:uppercase;letter-spacing:0.6px;font-weight:600;">Etiquetadas</div>
                          <div style="font-size:28px;color:#1e3a8a;font-weight:700;line-height:1.1;margin-top:4px;">{{totalEtiq}}</div>
                          <div style="font-size:11px;color:#3b82f6;font-weight:600;margin-top:2px;">{{pct}}%</div>
                        </div>
                      </td>
                      <td width="33%" style="padding:0 5px;">
                        <div style="background:linear-gradient(135deg,#f1f5f9 0%,#f8fafc 100%);border:1px solid #e2e8f0;border-radius:10px;padding:14px 12px;text-align:center;">
                          <div style="font-size:11px;color:#475569;text-transform:uppercase;letter-spacing:0.6px;font-weight:600;">Total convs</div>
                          <div style="font-size:28px;color:#0f172a;font-weight:700;line-height:1.1;margin-top:4px;">{{totalConv}}</div>
                          <div style="font-size:11px;color:#64748b;font-weight:600;margin-top:2px;">procesadas</div>
                        </div>
                      </td>
                      <td width="33%" style="padding:0 0 0 5px;">
                        <div style="background:linear-gradient(135deg,#fef3c7 0%,#fffbeb 100%);border:1px solid #fde68a;border-radius:10px;padding:14px 12px;text-align:center;">
                          <div style="font-size:11px;color:#a16207;text-transform:uppercase;letter-spacing:0.6px;font-weight:600;">Campañas</div>
                          <div style="font-size:28px;color:#78350f;font-weight:700;line-height:1.1;margin-top:4px;">{{campaigns.Count}}</div>
                          <div style="font-size:11px;color:#a16207;font-weight:600;margin-top:2px;">en el reporte</div>
                        </div>
                      </td>
                    </tr>
                  </table>
                </td></tr>

                <!-- Pie chart + leyenda -->
                {{chartSection}}

                <!-- Detalle por campaña -->
                <tr><td style="padding:8px 40px 8px;">
                  <h3 style="margin:0 0 12px;font-size:13px;color:#1e293b;text-transform:uppercase;letter-spacing:0.5px;font-weight:700;">Detalle por campaña</h3>
                  <table width="100%" cellpadding="0" cellspacing="0" style="border:1px solid #e2e8f0;border-radius:10px;background:#ffffff;overflow:hidden;">
                    {{campaignBlocks}}
                  </table>
                </td></tr>

                <!-- CTA -->
                <tr><td align="center" style="padding:28px 40px 8px;">
                  <a href="{{System.Net.WebUtility.HtmlEncode(excelUrl)}}"
                     style="display:inline-block;background:linear-gradient(135deg,#1d4ed8 0%,#3b82f6 100%);color:#ffffff;text-decoration:none;font-weight:600;font-size:14px;padding:14px 36px;border-radius:10px;box-shadow:0 4px 12px rgba(29,78,216,0.3);">
                    📊 Descargar reporte completo (Excel)
                  </a>
                  <p style="margin:14px 0 0;font-size:11px;color:#94a3b8;">
                    El enlace de descarga es válido por 48 horas.
                  </p>
                </td></tr>

                <!-- Footer -->
                <tr><td style="padding:24px 40px 32px;text-align:center;border-top:1px solid #e2e8f0;margin-top:16px;">
                  <p style="margin:0;font-size:11px;color:#94a3b8;line-height:1.6;">
                    Este es un mensaje automático de <strong style="color:#475569;">TalkIA</strong>. No respondas a este correo.
                  </p>
                </td></tr>
              </table>
            </td></tr>
          </table>
        </body>
        </html>
        """;

        return html;
    }

    /// <summary>
    /// Construye URL de QuickChart.io que renderiza un doughnut chart como PNG.
    /// Compatible con todos los clientes de email (incluyendo Gmail/Outlook que
    /// filtran SVG inline). El chart muestra distribución de etiquetas globales
    /// + segmento "Sin etiqueta" si aplica.
    /// </summary>
    private static string BuildPieChartUrl(
        IReadOnlyDictionary<string, int> labels,
        int unlabeledTotal,
        IReadOnlyDictionary<string, string> labelColors)
    {
        var labelsArr = labels.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
        var dataValues = labelsArr.Select(l => labels[l]).ToList();
        var colors = labelsArr.Select(l => labelColors[l]).ToList();

        if (unlabeledTotal > 0)
        {
            labelsArr.Add("Sin etiqueta");
            dataValues.Add(unlabeledTotal);
            colors.Add(UnlabeledColor);
        }

        // Chart.js config — versión compacta para URL.
        var config = new
        {
            type = "doughnut",
            data = new
            {
                labels = labelsArr,
                datasets = new[]
                {
                    new
                    {
                        data = dataValues,
                        backgroundColor = colors,
                        borderColor = "#ffffff",
                        borderWidth = 2,
                    },
                },
            },
            options = new
            {
                cutout = "60%",
                plugins = new
                {
                    legend = new { display = false },
                    datalabels = new
                    {
                        color = "#0f172a",
                        font = new { size = 11, weight = "bold" },
                        formatter = "(value) => value > 0 ? value : ''",
                    },
                },
            },
        };

        var json = System.Text.Json.JsonSerializer.Serialize(config);
        var encoded = Uri.EscapeDataString(json);

        // bkg=white para que se vea bien en clientes de email con dark mode también.
        return $"https://quickchart.io/chart?w=240&h=240&bkg=white&c={encoded}";
    }
}

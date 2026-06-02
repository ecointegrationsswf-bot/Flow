using System.Text;
using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Interfaces;
using AgentFlow.Infrastructure.Channels.UltraMsg;
using AgentFlow.Infrastructure.Email;
using AgentFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Infrastructure.ScheduledJobs;

/// <summary>
/// Executor del job diario WHATSAPP_LINE_HEALTH_CHECK (cron "0 6 * * *" Panamá).
///
/// Pingea TODAS las líneas WhatsApp activas vía UltraMsg /instance/status,
/// persiste el último estado en BD (para que la UI muestre badges reales sin
/// llamar a UltraMsg en cada render) y envía dos tipos de correo cuando hay
/// líneas caídas:
///   • Consolidado a TODOS los SuperAdmins activos — UN solo correo con
///     todas las líneas inactivas del sistema agrupadas por tenant.
///   • Por tenant a sus usuarios Admin/Supervisor — UN correo por tenant
///     listando las líneas caídas y QUÉ agentes IA quedan inactivos.
///
/// Resiliencia: ConsecutivePingFailures >= 2 antes de tratar como caída (tolera
/// 1 flake puntual). Si ninguna línea está caída no se envía NINGÚN correo
/// (cero ruido los días sanos).
/// </summary>
public class WhatsAppLineHealthCheckExecutor(
    AgentFlowDbContext db,
    IUltraMsgInstanceService ultra,
    IEmailService email,
    ITeamsNotifier teamsNotifier,
    ILogger<WhatsAppLineHealthCheckExecutor> log) : IScheduledJobExecutor
{
    public string Slug => "WHATSAPP_LINE_HEALTH_CHECK";

    private const int PingConcurrency = 5;
    private static readonly TimeSpan PingTimeout = TimeSpan.FromSeconds(5);
    private const int FailureThreshold = 2;
    private const string DownAlertEmoji = "⚠️";
    private const string OkEmoji = "✓";

    public async Task<JobRunResult> ExecuteAsync(
        ScheduledWebhookJob job, ScheduledJobContext ctx, CancellationToken ct)
    {
        // 1) Pingear todas las líneas activas.
        var lines = await db.Set<WhatsAppLine>()
            .Include(l => l.Tenant)
            .Where(l => l.IsActive)
            .ToListAsync(ct);

        if (lines.Count == 0)
            return JobRunResult.Skipped("Sin líneas WhatsApp activas para verificar.");

        var nowUtc = DateTime.UtcNow;
        using var sem = new SemaphoreSlim(PingConcurrency);

        await Task.WhenAll(lines.Select(async line =>
        {
            await sem.WaitAsync(ct);
            try { await PingLineAsync(line, nowUtc, ct); }
            finally { sem.Release(); }
        }));

        await db.SaveChangesAsync(ct);

        // 2) Identificar líneas con problema. Umbral bajado a 1: cualquier línea
        // no autenticada con al menos 1 fallo entra al reporte, separada en dos
        // niveles de severidad:
        //   • CRÍTICO  → ConsecutivePingFailures >= 2 (caída sostenida)
        //   • ATENCIÓN → ConsecutivePingFailures == 1 (primer fallo / standby)
        // En la práctica las líneas caídas se quedan en 1 fallo: el pre-chequeo
        // de salud corta el envío antes de usar la línea y UltraMsg alterna
        // authenticated/standby, así que el contador rara vez llega a 2. Por eso
        // reportamos también desde el primer fallo, sin perder la distinción de
        // severidad para el equipo.
        var alertLines = lines
            .Where(l => !string.Equals(l.LastStatus, "authenticated", StringComparison.OrdinalIgnoreCase)
                     && l.ConsecutivePingFailures >= 1)
            .ToList();

        if (alertLines.Count == 0)
        {
            log.LogInformation("[WhatsAppHealth] Todas las {Count} líneas conectadas. Sin correos.", lines.Count);
            return JobRunResult.Success(lines.Count,
                $"Verificadas={lines.Count} · Todas conectadas.");
        }

        var criticalLines = alertLines.Where(l => l.ConsecutivePingFailures >= FailureThreshold).ToList();
        var attentionLines = alertLines.Where(l => l.ConsecutivePingFailures < FailureThreshold).ToList();

        // ── Notificación a Microsoft Teams (Power Automate) ───────────
        // Antes del email a cada admin: un sólo mensaje consolidado a Teams
        // para el equipo de operaciones. Mucho más rápido que esperar a abrir
        // los emails y filtrar bandeja.
        try
        {
            var teamsSb = new StringBuilder();
            teamsSb.Append($"⚠️ Reporte diario WhatsApp · {criticalLines.Count} crítica(s), {attentionLines.Count} en atención");
            AppendTeamsSeverity(teamsSb, "🔴 CRÍTICO (≥2 fallos)", criticalLines);
            AppendTeamsSeverity(teamsSb, "🟡 ATENCIÓN (1 fallo)", attentionLines);
            await teamsNotifier.NotifyAsync(teamsSb.ToString(), ct);
        }
        catch (Exception ex) { log.LogWarning(ex, "[WhatsAppHealth] Teams notify falló."); }

        // 3) Notificación a SuperAdmins (1 correo consolidado).
        var superAdminEmails = await db.Set<SuperAdmin>()
            .Where(s => s.IsActive)
            .Select(s => s.Email)
            .ToListAsync(ct);

        if (superAdminEmails.Count > 0)
        {
            var (subjectSa, htmlSa) = RenderSuperAdminEmail(criticalLines, attentionLines, nowUtc);
            foreach (var to in superAdminEmails)
            {
                try { await email.SendCustomHtmlAsync(to, ccEmail: null, subjectSa, htmlSa, textBody: null, ct); }
                catch (Exception ex) { log.LogError(ex, "[WhatsAppHealth] Falló email a SuperAdmin {Email}.", to); }
            }
        }

        // 4) Notificación por tenant (1 correo por tenant a sus Admin/Supervisor).
        var tenantsWithDowns = alertLines
            .Where(l => l.TenantId.HasValue)
            .GroupBy(l => l.TenantId!.Value)
            .ToList();

        var tenantEmailsSent = 0;
        foreach (var grp in tenantsWithDowns)
        {
            var tenantId = grp.Key;
            var tenantLines = grp.ToList();
            var tenantName = tenantLines[0].Tenant?.Name ?? "Tu cuenta";

            // Cargar agentes afectados (por WhatsAppLineId de cada línea caída).
            var lineIds = tenantLines.Select(l => l.Id).ToHashSet();
            var affectedAgents = await db.Set<AgentDefinition>()
                .Where(a => a.IsActive
                         && a.TenantId == tenantId
                         && a.WhatsAppLineId.HasValue
                         && lineIds.Contains(a.WhatsAppLineId!.Value))
                .Select(a => new { a.WhatsAppLineId, a.Name, a.Type, a.AvatarName })
                .ToListAsync(ct);

            // Destinatarios: TODOS los usuarios activos del tenant (cualquier rol)
            // con email. Decisión del usuario: el aviso de línea caída debe llegar
            // a todo el equipo del corredor, no solo a Admin/Supervisor.
            var recipients = await db.Set<AppUser>()
                .Where(u => u.TenantId == tenantId
                         && u.IsActive
                         && u.Email != null && u.Email != "")
                .Select(u => u.Email)
                .ToListAsync(ct);

            if (recipients.Count == 0)
            {
                log.LogWarning("[WhatsAppHealth] Tenant {TenantId} ({Name}) sin destinatarios (usuarios activos con email).",
                    tenantId, tenantName);
                continue;
            }

            var (subjectT, htmlT) = RenderTenantEmail(
                tenantName, tenantLines,
                affectedAgents.Select(a => (a.WhatsAppLineId!.Value, a.AvatarName ?? a.Name, a.Type.ToString())).ToList(),
                nowUtc);

            foreach (var to in recipients)
            {
                try
                {
                    await email.SendCustomHtmlAsync(to, ccEmail: null, subjectT, htmlT, textBody: null, ct);
                    tenantEmailsSent++;
                }
                catch (Exception ex)
                {
                    log.LogError(ex, "[WhatsAppHealth] Falló email a {Email} tenant {Tenant}.", to, tenantName);
                }
            }
        }

        var summary = $"Verificadas={lines.Count} · Críticas={criticalLines.Count} · Atención={attentionLines.Count} · SA-emails={superAdminEmails.Count} · Tenant-emails={tenantEmailsSent}";
        log.LogWarning("[WhatsAppHealth] {Summary}", summary);
        return JobRunResult.Success(lines.Count, summary);
    }

    private async Task PingLineAsync(WhatsAppLine line, DateTime nowUtc, CancellationToken ct)
    {
        line.LastStatusCheckedAt = nowUtc;
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(PingTimeout);

            var result = await ultra.GetStatusAsync(line.InstanceId, line.ApiToken, timeoutCts.Token);
            var status = string.IsNullOrWhiteSpace(result.Status) ? "unknown" : result.Status.ToLowerInvariant();
            line.LastStatus = status;

            if (string.Equals(status, "authenticated", StringComparison.OrdinalIgnoreCase))
                line.ConsecutivePingFailures = 0;
            else
                line.ConsecutivePingFailures++;
        }
        catch (Exception ex)
        {
            // Error de red / timeout — no marcamos como "disconnected" en duro
            // (puede ser flake nuestro); subimos el contador y dejamos LastStatus
            // como estaba. Al cabo de 2 ticks fallidos se trata como caída real.
            line.ConsecutivePingFailures++;
            log.LogWarning(ex, "[WhatsAppHealth] Ping falló para {DisplayName} ({Instance}). Failures={Count}.",
                line.DisplayName, line.InstanceId, line.ConsecutivePingFailures);
        }
    }

    private static (string Subject, string Html) RenderSuperAdminEmail(
        List<WhatsAppLine> criticalLines, List<WhatsAppLine> attentionLines, DateTime nowUtc)
    {
        var dateStr = nowUtc.AddHours(-5).ToString("dd-MMM-yyyy HH:mm"); // Panamá UTC-5
        var total = criticalLines.Count + attentionLines.Count;

        var subject = $"{DownAlertEmoji} [AgentFlow] Reporte líneas WhatsApp — {criticalLines.Count} crítica(s), {attentionLines.Count} en atención";

        var sb = new StringBuilder();
        sb.Append("""
<!DOCTYPE html><html><body style="font-family:Arial,sans-serif;max-width:680px;margin:0 auto;padding:24px;color:#333;">
<table cellpadding="0" cellspacing="0" width="100%" style="background:#1a3a6b;color:#fff;padding:20px;border-radius:6px;margin-bottom:20px;">
  <tr><td bgcolor="#1a3a6b" style="background:#1a3a6b;color:#fff;padding:16px;">
    <h2 style="margin:0;color:#fff;">⚠️ Reporte diario de líneas WhatsApp</h2>
""");
        sb.Append($"<p style=\"margin:8px 0 0;color:#cfd8e3;\">Verificación de las 06:00 hora Panamá · {dateStr}</p>");
        sb.Append("</td></tr></table>");

        sb.Append($"<p>Se detectaron <b>{total} línea(s)</b> con problemas: <b>{criticalLines.Count}</b> crítica(s) — caída sostenida (≥2 fallos) — y <b>{attentionLines.Count}</b> en atención — primer fallo / posible parpadeo (1 fallo).</p>");

        if (criticalLines.Count > 0)
            AppendSeveritySection(sb, "🔴 Crítico — caída sostenida (≥2 fallos)", "#b91c1c", criticalLines);
        if (attentionLines.Count > 0)
            AppendSeveritySection(sb, "🟡 Atención — primer fallo / posible parpadeo (1 fallo)", "#b45309", attentionLines);

        sb.Append("<p style=\"margin-top:24px;color:#6b7280;font-size:12px;\">Este reporte se genera automáticamente cada día a las 06:00 hora Panamá. Solo se envía cuando hay líneas con problemas. «Crítico» = 2 o más verificaciones fallidas seguidas; «Atención» = primer fallo detectado (puede ser un parpadeo o una caída reciente).</p>");
        sb.Append("</body></html>");

        return (subject, sb.ToString());
    }

    /// <summary>
    /// Renderiza un bloque de severidad (Crítico / Atención) agrupando las líneas
    /// por tenant en tablas. Reutilizado por el correo consolidado a SuperAdmins.
    /// </summary>
    private static void AppendSeveritySection(
        StringBuilder sb, string sectionTitle, string accent, List<WhatsAppLine> sectionLines)
    {
        sb.Append($"<h2 style=\"margin:28px 0 6px;color:{accent};border-bottom:2px solid {accent};padding-bottom:4px;\">{sectionTitle}</h2>");
        var byTenant = sectionLines.GroupBy(l => l.Tenant?.Name ?? "(sin tenant)").OrderBy(g => g.Key).ToList();
        foreach (var grp in byTenant)
        {
            sb.Append($"<h3 style=\"margin:18px 0 8px;color:#1a3a6b;\">{HtmlEscape(grp.Key)}</h3>");
            sb.Append("<table cellpadding=\"6\" cellspacing=\"0\" border=\"0\" width=\"100%\" style=\"border-collapse:collapse;font-size:14px;\">");
            sb.Append("<thead><tr style=\"background:#f1f3f5;\"><th align=\"left\">Línea</th><th align=\"left\">Número</th><th align=\"left\">Instancia</th><th align=\"left\">Estado</th><th align=\"left\">Fallos</th><th align=\"left\">Verificada</th></tr></thead><tbody>");
            foreach (var line in grp.OrderBy(l => l.DisplayName))
            {
                var checkedAtStr = line.LastStatusCheckedAt.HasValue
                    ? line.LastStatusCheckedAt.Value.AddHours(-5).ToString("HH:mm")
                    : "—";
                sb.Append("<tr style=\"border-bottom:1px solid #e5e7eb;\">");
                sb.Append($"<td>{HtmlEscape(line.DisplayName)}</td>");
                sb.Append($"<td>{HtmlEscape(line.PhoneNumber)}</td>");
                sb.Append($"<td><code style=\"background:#f1f3f5;padding:2px 6px;border-radius:3px;\">{HtmlEscape(line.InstanceId)}</code></td>");
                sb.Append($"<td><b style=\"color:{accent};\">{HtmlEscape(line.LastStatus ?? "unknown")}</b></td>");
                sb.Append($"<td>{line.ConsecutivePingFailures}</td>");
                sb.Append($"<td>{checkedAtStr}</td>");
                sb.Append("</tr>");
            }
            sb.Append("</tbody></table>");
        }
    }

    /// <summary>
    /// Agrega un bloque de severidad al mensaje de texto plano de Teams. No hace
    /// nada si la lista está vacía (no ensucia el mensaje con secciones vacías).
    /// </summary>
    private static void AppendTeamsSeverity(StringBuilder sb, string label, List<WhatsAppLine> sectionLines)
    {
        if (sectionLines.Count == 0) return;
        sb.Append($"\n{label}:");
        foreach (var l in sectionLines.OrderBy(x => x.Tenant?.Name))
            sb.Append($"\n• {l.Tenant?.Name ?? "(sin tenant)"} → {l.DisplayName} ({l.PhoneNumber}) — {l.LastStatus ?? "unknown"}");
    }

    private static (string Subject, string Html) RenderTenantEmail(
        string tenantName,
        List<WhatsAppLine> tenantLines,
        List<(Guid LineId, string AgentName, string AgentType)> affectedAgents,
        DateTime nowUtc)
    {
        var dateStr = nowUtc.AddHours(-5).ToString("dd-MMM-yyyy HH:mm");
        var hasCritical = tenantLines.Any(l => l.ConsecutivePingFailures >= FailureThreshold);
        var primary = tenantLines[0].DisplayName;

        string subject;
        if (hasCritical)
            subject = tenantLines.Count == 1
                ? $"{DownAlertEmoji} Tu línea WhatsApp \"{primary}\" está desconectada"
                : $"{DownAlertEmoji} {tenantLines.Count} líneas WhatsApp desconectadas en tu cuenta";
        else
            subject = tenantLines.Count == 1
                ? $"{DownAlertEmoji} Posible problema con tu línea WhatsApp \"{primary}\""
                : $"{DownAlertEmoji} Posible problema con tus líneas WhatsApp";

        var sb = new StringBuilder();
        sb.Append("""
<!DOCTYPE html><html><body style="font-family:Arial,sans-serif;max-width:640px;margin:0 auto;padding:24px;color:#333;">
<table cellpadding="0" cellspacing="0" width="100%" style="background:#1a3a6b;color:#fff;border-radius:6px;margin-bottom:20px;">
  <tr><td bgcolor="#1a3a6b" style="background:#1a3a6b;color:#fff;padding:20px;">
""");
        sb.Append(hasCritical
            ? "    <h2 style=\"margin:0;color:#fff;\">⚠️ Línea WhatsApp desconectada</h2>"
            : "    <h2 style=\"margin:0;color:#fff;\">⚠️ Posible problema con tu línea WhatsApp</h2>");
        sb.Append($"<p style=\"margin:8px 0 0;color:#cfd8e3;\">Verificación diaria de las 06:00 hora Panamá · {dateStr}</p>");
        sb.Append("</td></tr></table>");

        sb.Append($"<p>Hola,</p><p>En la verificación diaria de hoy detectamos que <b>{(tenantLines.Count == 1 ? "una línea de WhatsApp" : "una o más líneas de WhatsApp")}</b> de <b>{HtmlEscape(tenantName)}</b> presenta(n) problemas de conexión.</p>");
        sb.Append("<p>Mientras una línea esté caída, los agentes IA asociados <b>no podrán enviar ni recibir mensajes</b>.</p>");

        // Crítico primero (más fallos), luego atención.
        foreach (var line in tenantLines.OrderByDescending(l => l.ConsecutivePingFailures).ThenBy(l => l.DisplayName))
        {
            var isCritical = line.ConsecutivePingFailures >= FailureThreshold;
            var boxBorder = isCritical ? "#fca5a5" : "#fcd34d";
            var boxBg = isCritical ? "#fef2f2" : "#fffbeb";
            var titleColor = isCritical ? "#991b1b" : "#92400e";
            var badge = isCritical ? "📵 Desconectada" : "⏳ Primer fallo (en revisión)";

            sb.Append($"<div style=\"border:1px solid {boxBorder};background:{boxBg};border-radius:6px;padding:16px;margin:16px 0;\">");
            sb.Append($"<h3 style=\"margin:0 0 8px;color:{titleColor};\">{badge} — {HtmlEscape(line.DisplayName)}</h3>");
            sb.Append("<table cellpadding=\"4\" cellspacing=\"0\" style=\"font-size:14px;\">");
            sb.Append($"<tr><td style=\"color:#6b7280;\">Número:</td><td><b>{HtmlEscape(line.PhoneNumber)}</b></td></tr>");
            sb.Append($"<tr><td style=\"color:#6b7280;\">Estado:</td><td><b style=\"color:#b91c1c;\">{HtmlEscape(line.LastStatus ?? "unknown")}</b></td></tr>");
            if (line.LastStatusCheckedAt.HasValue)
                sb.Append($"<tr><td style=\"color:#6b7280;\">Verificada:</td><td>{line.LastStatusCheckedAt.Value.AddHours(-5):dd-MMM HH:mm}</td></tr>");
            sb.Append("</table>");

            var agentsForLine = affectedAgents.Where(a => a.LineId == line.Id).ToList();
            if (agentsForLine.Count > 0)
            {
                sb.Append("<p style=\"margin:12px 0 4px;color:#92400e;\"><b>Agentes IA que quedan inactivos:</b></p><ul style=\"margin:0;padding-left:20px;\">");
                foreach (var (_, agentName, agentType) in agentsForLine)
                    sb.Append($"<li>{HtmlEscape(agentName)} — Agente de {HtmlEscape(agentType)}</li>");
                sb.Append("</ul>");
            }
            else
            {
                sb.Append("<p style=\"margin:12px 0 0;color:#6b7280;font-size:13px;\">(Ningún agente IA activo está vinculado a esta línea.)</p>");
            }
            sb.Append("</div>");
        }

        sb.Append("""
<div style="margin:24px 0;padding:16px;background:#f0f9ff;border-left:4px solid #1a3a6b;border-radius:4px;">
  <p style="margin:0 0 8px;"><b>Para reconectar:</b></p>
  <ol style="margin:0;padding-left:20px;">
    <li>Ingresa al portal de AgentFlow.</li>
    <li>Ve a <b>Configuración → WhatsApp</b>.</li>
    <li>Presiona <b>Vincular</b> en la línea afectada y escanea el QR desde el WhatsApp del número.</li>
  </ol>
</div>
<p style="margin-top:24px;color:#6b7280;font-size:12px;">Este aviso se envía una vez al día mientras la línea tenga problemas. Recibís este correo porque sos usuario de AgentFlow en esta cuenta.</p>
</body></html>
""");

        return (subject, sb.ToString());
    }

    private static string HtmlEscape(string? s) =>
        string.IsNullOrEmpty(s) ? "" : System.Net.WebUtility.HtmlEncode(s);
}

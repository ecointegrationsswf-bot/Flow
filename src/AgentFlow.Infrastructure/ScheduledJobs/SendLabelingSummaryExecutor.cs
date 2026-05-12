using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Enums;
using AgentFlow.Domain.Interfaces;
using AgentFlow.Infrastructure.Email;
using AgentFlow.Infrastructure.Persistence;
using AgentFlow.Infrastructure.Storage;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Infrastructure.ScheduledJobs;

/// <summary>
/// Executor del job SEND_LABELING_SUMMARY. Está diseñado para correr una hora
/// después del job de etiquetado IA (típicamente Cron "5 12 * * *" Panamá si
/// el etiquetado corre a "5 11 * * *").
///
/// Por cada usuario que cargó campañas con cambios desde el último envío:
///   1. Carga TODAS las conversaciones de SUS campañas (con o sin etiqueta).
///   2. Genera UN solo Excel consolidado (mismo formato que el ejemplo del cliente).
///   3. Sube el Excel al container "sumary" de Azure Blob.
///   4. Envía email con conteo por etiqueta + botón al Excel.
///   5. Marca Campaign.LabelingSummarySentAt en cada campaña incluida.
///
/// Idempotencia: una campaña se incluye solo si tiene al menos una conversación
/// con LabeledAt > LabelingSummarySentAt (o LabelingSummarySentAt NULL). Tras
/// enviar, el timestamp se actualiza al UtcNow para que la próxima corrida
/// solo dispare con cambios nuevos.
/// </summary>
public class SendLabelingSummaryExecutor(
    AgentFlowDbContext db,
    IBlobStorageService blobStorage,
    IEmailService emailService,
    JobExecutionAuditor auditor,
    ILogger<SendLabelingSummaryExecutor> log) : IScheduledJobExecutor
{
    public string Slug => "SEND_LABELING_SUMMARY";

    private const string ContainerName = "sumary";

    public async Task<JobRunResult> ExecuteAsync(
        ScheduledWebhookJob job, ScheduledJobContext ctx, CancellationToken ct)
    {
        // 0. Emails de super admins activos para BCC silencioso en cada envío.
        var superAdminEmails = await db.SuperAdmins
            .Where(s => s.IsActive && !string.IsNullOrEmpty(s.Email))
            .Select(s => s.Email)
            .ToListAsync(ct);

        // 1. Detectar campañas con cambios desde el último envío.
        // Una campaña tiene cambios si alguna de sus conversaciones tiene
        // LabeledAt > LabelingSummarySentAt (o el timestamp es NULL = nunca enviado).
        var campaignsWithChanges = await db.Campaigns
            .Where(c => !string.IsNullOrEmpty(c.LaunchedByUserId)
                        && db.Conversations.Any(conv =>
                            conv.CampaignId == c.Id
                            && conv.LabeledAt != null
                            && (c.LabelingSummarySentAt == null || conv.LabeledAt > c.LabelingSummarySentAt)))
            .Select(c => new { c.Id, c.Name, c.TenantId, c.LaunchedByUserId })
            .ToListAsync(ct);

        if (campaignsWithChanges.Count == 0)
            return JobRunResult.Skipped("Sin campañas con cambios desde el último envío.");

        // 2. Agrupar por (usuario, tenant) — CRÍTICO: agrupar solo por
        //    LaunchedByUserId mezcla campañas de diferentes tenants en un único
        //    grupo cuando el marker se repite (ej: 'system:download' aparece en
        //    todos los tenants que tienen descarga de morosidad). Eso provocaba
        //    un data leak: el Excel del grupo mezclaba campañas de tenants
        //    distintos y se enviaba a los usuarios de un solo tenant.
        //    Agrupando por (UserId, TenantId) garantizamos que cada grupo es de
        //    un único tenant y los recipients corresponden al MISMO tenant.
        var byUserAndTenant = campaignsWithChanges
            .GroupBy(c => new { UserKey = c.LaunchedByUserId!, c.TenantId })
            .ToList();

        var totalUsers = byUserAndTenant.Count;
        var sent = 0;
        var failed = 0;

        foreach (var group in byUserAndTenant)
        {
            if (ct.IsCancellationRequested) break;

            var key = group.Key.UserKey;
            var groupTenantId = group.Key.TenantId;
            var campaignIds = group.Select(g => g.Id).ToList();

            // ── Resolver destinatarios del resumen ─────────────────────────────
            // Caso A — Marcador automático (system, system:download, system:cron, etc.):
            //   No hay un usuario humano "owner". Enviamos el resumen a TODOS los
            //   AppUsers activos del tenant con email válido. Esto cubre las campañas
            //   creadas por descargas automáticas de morosidad u otros jobs internos
            //   sin generar fallos cuando el "usuario" no es humano.
            // Caso B — Usuario humano (LaunchedByUserId con Guid, email o nombre):
            //   Lo resolvemos individualmente en AppUsers o SuperAdmins (legacy).
            var isAutomated = string.IsNullOrEmpty(key)
                || key.Equals("system", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith("system:", StringComparison.OrdinalIgnoreCase);

            List<(string FullName, string Email)> recipients;

            if (isAutomated)
            {
                var rows = await db.AppUsers
                    .Where(u => u.TenantId == groupTenantId
                                && u.IsActive
                                && !string.IsNullOrEmpty(u.Email))
                    .Select(u => new { u.FullName, u.Email })
                    .ToListAsync(ct);

                recipients = rows.Select(x => (FullName: x.FullName, Email: x.Email)).ToList();

                if (recipients.Count == 0)
                {
                    // No hay usuarios activos con email en el tenant — no es un fallo
                    // (es un caso esperado de un tenant en setup); solo loguear y omitir.
                    log.LogInformation(
                        "Summary: marcador automático '{Key}' sin AppUsers activos con email en tenant {TenantId} — {Count} campañas omitidas.",
                        key, groupTenantId, group.Count());
                    continue;
                }

                log.LogInformation(
                    "Summary: marcador automático '{Key}' → enviando resumen a {Count} usuarios del tenant {TenantId} ({Campaigns} campañas).",
                    key, recipients.Count, groupTenantId, group.Count());
            }
            else
            {
                var user = await db.AppUsers
                    .Where(u => (u.Id.ToString() == key || u.Email == key || u.FullName == key)
                                && !string.IsNullOrEmpty(u.Email))
                    .Select(u => new { u.FullName, u.Email })
                    .FirstOrDefaultAsync(ct);

                if (user is null)
                {
                    var sa = await db.SuperAdmins
                        .Where(s => (s.Id.ToString() == key || s.Email == key || s.FullName == key)
                                    && s.IsActive
                                    && !string.IsNullOrEmpty(s.Email))
                        .Select(s => new { s.FullName, s.Email })
                        .FirstOrDefaultAsync(ct);
                    if (sa is not null) user = sa;
                }

                if (user is null || string.IsNullOrEmpty(user.Email))
                {
                    log.LogWarning("Summary: usuario '{UserKey}' no encontrado o sin email — {Count} campañas omitidas.",
                        key, group.Count());
                    failed++;
                    auditor.RecordFailure(
                        ctx.ExecutionId, groupTenantId,
                        JobExecutionAuditor.ContextTypes.User, key, key,
                        $"Usuario '{key}' no encontrado en AppUsers ni SuperAdmins, o sin email — {group.Count()} campañas omitidas.");
                    continue;
                }

                recipients = new List<(string FullName, string Email)>
                {
                    (FullName: user.FullName, Email: user.Email)
                };
            }

            // ── Enviar resumen ────────────────────────────────────────────────
            try
            {
                var (excelBytes, perCampaignSummary) = await BuildExcelAndSummaryAsync(campaignIds, ct);

                // Folder en blob — para automatizadas, namespace por tenant; para
                // humanas, namespace por email del usuario (legacy).
                var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                var safeFolder = isAutomated
                    ? $"system_{groupTenantId:N}"
                    : SafeFolderName(recipients[0].Email);
                var blobPath = $"{safeFolder}/Reporte_{stamp}.xlsx";
                var url = await blobStorage.UploadAndGetSasUrlAsync(
                    ContainerName, blobPath, excelBytes,
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    TimeSpan.FromDays(2), ct);

                // Enviar email a cada destinatario (con copia oculta a super admins).
                // Si falla un destinatario individual, no abortamos el grupo entero —
                // contamos parciales para que el job no se desactive por casos edge.
                var perGroupSent = 0;
                var perGroupFailed = 0;
                foreach (var r in recipients)
                {
                    try
                    {
                        await emailService.SendLabelingSummaryAsync(
                            r.Email, r.FullName, url, perCampaignSummary,
                            bccEmails: superAdminEmails, ct: ct);
                        perGroupSent++;
                    }
                    catch (Exception exMail)
                    {
                        perGroupFailed++;
                        log.LogError(exMail,
                            "Summary: error enviando email a {Email} (grupo '{Key}', tenant {TenantId}).",
                            r.Email, key, groupTenantId);
                        auditor.RecordFailure(
                            ctx.ExecutionId, groupTenantId,
                            JobExecutionAuditor.ContextTypes.User, r.Email,
                            $"{r.FullName} <{r.Email}>", exMail.Message);
                    }
                }

                if (perGroupSent > 0)
                {
                    // Solo marcamos las campañas como "enviadas" si al menos un
                    // destinatario recibió el correo, para no perder reintentos
                    // cuando el envío falla totalmente.
                    await db.Campaigns
                        .Where(c => campaignIds.Contains(c.Id))
                        .ExecuteUpdateAsync(s => s.SetProperty(c => c.LabelingSummarySentAt, DateTime.UtcNow), ct);

                    log.LogInformation(
                        "Summary: grupo '{Key}' enviado a {Sent}/{Total} destinatarios con {Count} campañas. URL={Url}",
                        key, perGroupSent, recipients.Count, campaignIds.Count, url);
                    sent++;
                }
                else
                {
                    failed++;
                }
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Summary: error procesando grupo '{Key}'.", key);
                failed++;
                auditor.RecordFailure(
                    ctx.ExecutionId, groupTenantId,
                    JobExecutionAuditor.ContextTypes.User, key,
                    isAutomated ? $"[automated] {key}" : key,
                    ex.Message);
            }
        }

        await auditor.FlushAsync(ct);

        var summary = $"Usuarios procesados={totalUsers} · OK={sent} · Fallos={failed}";
        log.LogInformation("SendLabelingSummary completo: {Summary}", summary);

        if (failed == 0) return JobRunResult.Success(totalUsers, summary);
        if (sent > 0) return JobRunResult.Partial(totalUsers, sent, failed, summary);
        return JobRunResult.Failed("Todos los envíos fallaron.", summary);
    }

    private async Task<(byte[] Bytes, IReadOnlyList<(string CampaignName, IReadOnlyDictionary<string, int> CountsByLabel, int Unlabeled)> Summary)>
        BuildExcelAndSummaryAsync(List<Guid> campaignIds, CancellationToken ct)
    {
        // Cargar todos los datos necesarios en memoria. Para volúmenes grandes
        // habría que paginar, pero el job nocturno raramente excede unos miles
        // de filas por usuario.
        //
        // FILTRO DE IDEMPOTENCIA — solo etiquetadas DESDE el último envío de la
        // campaña. El correo debe contener solo el delta del día (no acumulado
        // histórico). Si la campaña nunca se reportó (LabelingSummarySentAt
        // NULL), entran todas las etiquetadas; si ya se reportó, solo las
        // posteriores a esa fecha.
        var rows = await (
            from conv in db.Conversations
            join camp in db.Campaigns on conv.CampaignId equals camp.Id
            join cc in db.CampaignContacts on new { CampId = conv.CampaignId!.Value, conv.ClientPhone }
                                       equals new { CampId = cc.CampaignId, ClientPhone = cc.PhoneNumber } into ccLeft
            from cc in ccLeft.DefaultIfEmpty()
            join lbl in db.ConversationLabels on conv.LabelId equals lbl.Id into lblLeft
            from lbl in lblLeft.DefaultIfEmpty()
            join agent in db.AgentDefinitions on conv.ActiveAgentId equals agent.Id into agentLeft
            from agent in agentLeft.DefaultIfEmpty()
            where conv.CampaignId != null && campaignIds.Contains(conv.CampaignId.Value)
               && conv.LabeledAt != null
               && (camp.LabelingSummarySentAt == null || conv.LabeledAt > camp.LabelingSummarySentAt)
            select new
            {
                CampaignName = camp.Name,
                ClientName = cc != null ? cc.ClientName : conv.ClientName,
                conv.ClientPhone,
                Identification = cc != null ? cc.PolicyNumber : null,
                LastActivity = conv.LastActivityAt,
                LabelName = lbl != null ? lbl.Name : null,
                conv.LabeledAt,
                ConversationId = conv.Id,
                LaunchUserName = (string?)null, // se rellena fuera de la query
                AgentName = agent != null ? agent.Name : null,
            }).ToListAsync(ct);

        // Cargar usuarios que cargaron las campañas (por LaunchedByUserId).
        var userMap = await db.Campaigns
            .Where(c => campaignIds.Contains(c.Id))
            .Select(c => new { c.Id, c.LaunchedByUserId })
            .ToListAsync(ct);
        var userIds = userMap.Select(u => u.LaunchedByUserId).Where(u => !string.IsNullOrEmpty(u)).Distinct().ToList();
        var users = await db.AppUsers
            .Where(u => userIds.Contains(u.Id.ToString()) || userIds.Contains(u.Email))
            .Select(u => new { u.Id, u.FullName, FirstName = u.FullName, u.Email })
            .ToListAsync(ct);

        // Cargar resúmenes (último mensaje del agente al cliente como aproximación al "Resumen").
        var conversationIds = rows.Select(r => r.ConversationId).Distinct().ToList();
        var summaries = await db.Messages
            .Where(m => conversationIds.Contains(m.ConversationId)
                        && m.Direction == MessageDirection.Outbound
                        && m.IsFromAgent)
            .GroupBy(m => m.ConversationId)
            .Select(g => new
            {
                ConvId = g.Key,
                Last = g.OrderByDescending(m => m.SentAt).FirstOrDefault()
            })
            .ToDictionaryAsync(x => x.ConvId, x => x.Last != null ? x.Last.Content : "", ct);

        // Build Excel con ClosedXML.
        using var wb = new XLWorkbook();
        var sheet = wb.Worksheets.Add("Reporte");

        var headers = new[] { "Campaña", "Cliente", "Celular", "Identificación", "Fecha Gestión",
                              "Etiqueta", "Resumen de la conversación", "Usuario", "Apellido", "Agente" };
        for (int i = 0; i < headers.Length; i++)
        {
            var cell = sheet.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1E40AF");
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        // Mapeos auxiliares
        var userByCampaign = userMap.ToDictionary(u => u.Id, u => u.LaunchedByUserId);
        var userInfoByKey = users.ToDictionary(u => (string?)u.Id.ToString() ?? u.Email,
                                               u => (FullName: u.FullName, Email: u.Email),
                                               StringComparer.OrdinalIgnoreCase);

        int row = 2;
        foreach (var r in rows.OrderBy(x => x.CampaignName).ThenByDescending(x => x.LastActivity))
        {
            var (firstName, lastName) = SplitName(ResolveLaunchUserName(rows.First(x => x.CampaignName == r.CampaignName).ConversationId, r.CampaignName, userByCampaign, userInfoByKey, db, campaignIds));
            sheet.Cell(row, 1).Value = r.CampaignName ?? "";
            sheet.Cell(row, 2).Value = r.ClientName ?? "";
            sheet.Cell(row, 3).Value = r.ClientPhone ?? "";
            sheet.Cell(row, 4).Value = r.Identification ?? "";
            sheet.Cell(row, 5).Value = (r.LabeledAt ?? r.LastActivity).ToString("dd/MM/yyyy");
            sheet.Cell(row, 6).Value = r.LabelName ?? "";
            sheet.Cell(row, 7).Value = summaries.TryGetValue(r.ConversationId, out var s) ? (s ?? "") : "";
            sheet.Cell(row, 8).Value = firstName;
            sheet.Cell(row, 9).Value = lastName;
            sheet.Cell(row, 10).Value = r.AgentName ?? "";
            row++;
        }
        sheet.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        var bytes = ms.ToArray();

        // Resumen por campaña: { campaignName: { label: count }, unlabeled }
        var perCampaign = rows
            .GroupBy(r => r.CampaignName ?? "(sin nombre)")
            .Select(g =>
            {
                var counts = g.Where(x => x.LabelName != null)
                              .GroupBy(x => x.LabelName!)
                              .ToDictionary(x => x.Key, x => x.Count());
                var unlabeled = g.Count(x => x.LabelName == null);
                return (CampaignName: g.Key,
                        CountsByLabel: (IReadOnlyDictionary<string, int>)counts,
                        Unlabeled: unlabeled);
            })
            .OrderBy(x => x.CampaignName)
            .ToList();

        return (bytes, perCampaign);
    }

    /// <summary>
    /// Resuelve el nombre del usuario que cargó la campaña a partir del mapeo.
    /// Devuelve string vacío si no hay usuario o no se pudo resolver.
    /// </summary>
    private static string ResolveLaunchUserName(
        Guid conversationId, string campaignName,
        Dictionary<Guid, string?> userByCampaign,
        Dictionary<string?, (string FullName, string Email)> userInfoByKey,
        AgentFlowDbContext _db, List<Guid> _campaignIds)
    {
        // Heurística simple: tomar cualquiera de los usuarios mapeados al campaignId que coincide por nombre.
        // El mapeo exacto sería por conversation→campaign, pero como ya filtramos por campaignIds estos
        // son seguros. Mantener la query simple en memoria.
        var pair = userByCampaign.FirstOrDefault();
        if (pair.Value is null) return string.Empty;
        if (userInfoByKey.TryGetValue(pair.Value, out var info))
            return info.FullName;
        return string.Empty;
    }

    /// <summary>
    /// Sanitiza un string para usarlo como nombre de folder/path en Azure Blob.
    /// Reemplaza todo lo que no sea letra/dígito/guión bajo/guión por '_', quita
    /// puntos finales y caracteres reservados de URL. Resultado seguro para
    /// concatenar en URLs sin URL-encoding.
    /// </summary>
    private static string SafeFolderName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "user";
        var sb = new System.Text.StringBuilder(raw.Length);
        foreach (var ch in raw.Trim())
        {
            if (char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' || ch == '.')
                sb.Append(ch);
            else
                sb.Append('_');
        }
        var s = sb.ToString().Trim('.', '_');
        return s.Length == 0 ? "user" : s;
    }

    /// <summary>
    /// Divide "JOHN SMITH" → ("JOHN", "SMITH"). Si solo hay una palabra, va al primer campo.
    /// </summary>
    private static (string First, string Last) SplitName(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName)) return ("", "");
        var trimmed = fullName.Trim();
        var lastSpace = trimmed.LastIndexOf(' ');
        if (lastSpace < 0) return (trimmed, "");
        return (trimmed[..lastSpace].Trim(), trimmed[(lastSpace + 1)..].Trim());
    }
}

using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Enums;
using AgentFlow.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AgentFlow.Infrastructure.Persistence.Repositories;

public class ConversationRepository(AgentFlowDbContext db) : IConversationRepository
{
    public async Task<Conversation?> GetActiveByPhoneAsync(Guid tenantId, string phone, CancellationToken ct = default)
        => await db.Conversations
            .Include(c => c.Messages)
            .FirstOrDefaultAsync(c => c.TenantId == tenantId
                && c.ClientPhone == phone
                && c.Status != ConversationStatus.Closed, ct);

    public async Task<Conversation?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await db.Conversations
            .Include(c => c.Messages)
            .Include(c => c.Campaign)   // para mostrar el nombre de campaña en el monitor
            .FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task<IEnumerable<Conversation>> GetActiveByTenantAsync(
        Guid tenantId,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        string? launchedByUserId = null,
        CancellationToken ct = default)
    {
        var q = db.Conversations
            .Include(c => c.ActiveAgent)   // necesario para agentType/agentName en el monitor
            .Include(c => c.Messages)      // necesario para lastMessagePreview
            .Where(c => c.TenantId == tenantId);

        // Filtro por fecha civil en zona horaria del tenant (Panamá = UTC-5, sin DST).
        // DATE(LastActivityAt - 5h) BETWEEN DATE(@from) AND DATE(@to) — así una
        // conversación a las 22:20 PA del 04/05 (que en UTC quedó como 03:20 del 05/05)
        // aparece bajo el filtro 04/05 PA, junto con las del 13:00 PA del mismo día.
        // EF Core traduce DateTime.AddHours(...).Date a CAST(DATEADD(hour, -5, ...) AS DATE).
        const int paOffsetHours = -5;
        if (fromUtc.HasValue) q = q.Where(c => c.LastActivityAt.AddHours(paOffsetHours).Date >= fromUtc.Value.Date);
        if (toUtc.HasValue)   q = q.Where(c => c.LastActivityAt.AddHours(paOffsetHours).Date <= toUtc.Value.Date);

        if (!string.IsNullOrWhiteSpace(launchedByUserId))
        {
            // Modos de filtrado del monitor (centinelas + clave de usuario):
            //   "__inbound__"    → SOLO conversaciones sin campaña (el cliente escribió
            //                      espontáneamente, no salió de una campaña).
            //   "__unassigned__" → campañas de descarga sin ejecutivo matcheado
            //                      (LaunchedByUserId = "system:download").
            //   <email/clave>    → SOLO las campañas de ESE usuario (ya NO se mezclan
            //                      las conversaciones sin campaña — antes el OR las traía
            //                      siempre, lo que confundía el conteo por ejecutivo).
            // null/"" (sin filtro) → todas las del tenant (rama de arriba, no entra acá).
            var userKey = launchedByUserId.Trim();
            if (userKey == "__inbound__")
            {
                q = q.Where(c => c.CampaignId == null);
            }
            else if (userKey == "__unassigned__")
            {
                q = q.Where(c => c.CampaignId != null
                              && db.Campaigns.Any(camp => camp.Id == c.CampaignId
                                                       && camp.LaunchedByUserId == "system:download"));
            }
            else
            {
                // El userKey que llega del Monitor puede ser el id (GUID), el email
                // o el fullName del usuario. Y lo que se guardó en
                // Campaign.LaunchedByUserId también varía: id en campañas NUEVAS
                // (a partir del fix de jun-2026) y fullName/email en campañas
                // VIEJAS. Para que "Mis conversaciones" muestre TODAS sus campañas
                // sin importar el formato, resolvemos al usuario y matcheamos contra
                // todos sus identificadores conocidos.
                var keys = new List<string> { userKey };

                var appUser = await db.AppUsers
                    .Where(u => u.Id.ToString() == userKey || u.Email == userKey || u.FullName == userKey)
                    .Select(u => new { u.Id, u.Email, u.FullName })
                    .FirstOrDefaultAsync(ct);
                if (appUser != null)
                {
                    keys.Add(appUser.Id.ToString());
                    if (!string.IsNullOrWhiteSpace(appUser.Email)) keys.Add(appUser.Email);
                    if (!string.IsNullOrWhiteSpace(appUser.FullName)) keys.Add(appUser.FullName);
                }
                else
                {
                    var sa = await db.SuperAdmins
                        .Where(s => s.Id.ToString() == userKey || s.Email == userKey || s.FullName == userKey)
                        .Select(s => new { s.Id, s.Email, s.FullName })
                        .FirstOrDefaultAsync(ct);
                    if (sa != null)
                    {
                        keys.Add(sa.Id.ToString());
                        if (!string.IsNullOrWhiteSpace(sa.Email)) keys.Add(sa.Email);
                        if (!string.IsNullOrWhiteSpace(sa.FullName)) keys.Add(sa.FullName);
                    }
                }

                var keyList = keys.Distinct().ToList();
                q = q.Where(c => c.CampaignId != null
                              && db.Campaigns.Any(camp => camp.Id == c.CampaignId
                                                       && camp.LaunchedByUserId != null
                                                       && keyList.Contains(camp.LaunchedByUserId)));
            }
        }

        return await q
            .OrderByDescending(c => c.LastActivityAt)
            .Take(200)                     // límite para no sobrecargar el monitor
            .ToListAsync(ct);
    }

    public async Task<Conversation> CreateAsync(Conversation conversation, CancellationToken ct = default)
    {
        db.Conversations.Add(conversation);
        await db.SaveChangesAsync(ct);
        return conversation;
    }

    public async Task UpdateAsync(Conversation conversation, CancellationToken ct = default)
    {
        var entry = db.Entry(conversation);
        if (entry.State == EntityState.Detached)
            db.Conversations.Update(conversation);

        await db.SaveChangesAsync(ct);
    }

    public async Task AddMessageAsync(Message message, CancellationToken ct = default)
    {
        db.Set<Message>().Add(message);
    }

    public async Task AddGestionEventAsync(GestionEvent ev, CancellationToken ct = default)
    {
        db.Set<GestionEvent>().Add(ev);
        await Task.CompletedTask;
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        await db.SaveChangesAsync(ct);
    }

    public async Task<IEnumerable<Conversation>> GetByStatusAsync(Guid tenantId, ConversationStatus status, CancellationToken ct = default)
        => await db.Conversations
            .Where(c => c.TenantId == tenantId && c.Status == status)
            .OrderByDescending(c => c.LastActivityAt)
            .ToListAsync(ct);

    public async Task<Campaign?> GetCampaignAsync(Guid campaignId, CancellationToken ct = default)
        => await db.Campaigns.FirstOrDefaultAsync(c => c.Id == campaignId, ct);

    public async Task<Conversation?> GetLatestByPhoneAsync(Guid tenantId, string phone, CancellationToken ct = default)
        => await db.Conversations
            .Include(c => c.Messages)
            .Where(c => c.TenantId == tenantId && c.ClientPhone == phone)
            .OrderByDescending(c => c.LastActivityAt)
            .FirstOrDefaultAsync(ct);
}

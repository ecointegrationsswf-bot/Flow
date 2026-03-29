using System.Text.RegularExpressions;
using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Enums;
using AgentFlow.Domain.Interfaces;
using MediatR;

namespace AgentFlow.Application.Modules.Campaigns;

/// <summary>
/// Handler que procesa el comando StartCampaignCommand.
/// Crea la campaña y sus contactos en la base de datos.
///
/// Flujo:
/// 1. Recibe la lista de contactos del archivo Excel procesado
/// 2. Valida y normaliza cada teléfono al formato E.164 panameño (+507XXXXXXXX)
/// 3. Detecta duplicados (mismo teléfono en el mismo archivo)
/// 4. Crea la entidad Campaign con metadata (nombre, agente, canal, etc.)
/// 5. Crea un CampaignContact por cada contacto válido
/// 6. Guarda todo en la base de datos en una sola transacción
/// 7. Retorna el ID de la campaña creada
/// </summary>
public class StartCampaignCommandHandler(ICampaignRepository campaigns) : IRequestHandler<StartCampaignCommand, Guid>
{
    public async Task<Guid> Handle(StartCampaignCommand cmd, CancellationToken ct)
    {
        // ── 1. Crear la campaña ──────────────────────────────
        var campaign = new Campaign
        {
            Id = Guid.NewGuid(),
            TenantId = cmd.TenantId,
            Name = cmd.Name,
            AgentDefinitionId = cmd.AgentDefinitionId,
            Channel = cmd.Channel,
            Trigger = cmd.Trigger,
            IsActive = true,
            ScheduledAt = cmd.ScheduledAt,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = cmd.CreatedByUserId,
            Contacts = []
        };

        // ── 2. Procesar contactos ────────────────────────────
        // HashSet para detectar teléfonos duplicados en el mismo archivo
        var seenPhones = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in cmd.Contacts)
        {
            // Normalizar teléfono al formato E.164 panameño
            var (normalizedPhone, isValid) = NormalizePanamanianPhone(row.PhoneNumber);

            // Detectar duplicados: si el teléfono ya apareció, marcarlo como inválido
            var isDuplicate = !seenPhones.Add(normalizedPhone);

            var contact = new CampaignContact
            {
                Id = Guid.NewGuid(),
                CampaignId = campaign.Id,
                PhoneNumber = normalizedPhone,
                ClientName = row.ClientName?.Trim(),
                Email = row.Email?.Trim(),
                PolicyNumber = row.PolicyNumber?.Trim(),
                InsuranceCompany = row.InsuranceCompany?.Trim(),
                PendingAmount = row.PendingAmount,
                ExtraData = row.Extra ?? new Dictionary<string, string>(),
                IsPhoneValid = isValid && !isDuplicate,
                RetryCount = 0,
                Result = GestionResult.Pending,
                CreatedAt = DateTime.UtcNow
            };

            campaign.Contacts.Add(contact);
        }

        // ── 3. Actualizar contadores ─────────────────────────
        campaign.TotalContacts = campaign.Contacts.Count;
        campaign.ProcessedContacts = 0;

        // ── 4. Guardar en BD (una sola transacción) ──────────
        await campaigns.CreateWithContactsAsync(campaign, ct);

        return campaign.Id;
    }

    /// <summary>
    /// Normaliza un teléfono al formato E.164 panameño (+507XXXXXXXX).
    ///
    /// Reglas de Panamá:
    /// - Celulares: +507 6XXX-XXXX (8 dígitos, empiezan con 6)
    /// - Fijos: +507 2XX-XXXX o 3XX-XXXX (7 dígitos)
    ///
    /// Ejemplos:
    /// "60001234"       → "+50760001234" (agrega código de país)
    /// "+507 6000-1234" → "+50760001234" (limpia espacios/guiones)
    /// "507-6000-1234"  → "+50760001234" (limpia y agrega +)
    /// "0060001234"     → inválido (solo ceros)
    /// "+507507"        → inválido (bug conocido de TalkIA)
    /// </summary>
    internal static (string NormalizedPhone, bool IsValid) NormalizePanamanianPhone(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return ("", false);

        // Limpiar todo excepto dígitos
        var digits = Regex.Replace(raw.Trim(), @"[^\d]", "");

        // Si tiene código de país 507, quitarlo
        if (digits.StartsWith("507") && digits.Length > 7)
            digits = digits[3..];

        // Validaciones
        if (digits.Length < 7) return ($"+507{digits}", false);
        if (digits.Length > 8) return ($"+507{digits}", false);
        if (digits.All(c => c == '0')) return ($"+507{digits}", false);
        if (digits == "507") return ("+507507", false);

        // Celulares: 8 dígitos empezando con 6
        // Fijos: 7 dígitos empezando con 2 o 3
        var isValidFormat = digits.Length switch
        {
            8 => digits[0] == '6',
            7 => digits[0] is '2' or '3',
            _ => false
        };

        return ($"+507{digits}", isValidFormat);
    }
}

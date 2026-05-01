using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using AgentFlow.Domain.Entities;
using Microsoft.AspNetCore.RateLimiting;
using AgentFlow.Infrastructure.Channels.UltraMsg;
using AgentFlow.Infrastructure.Email;
using AgentFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace AgentFlow.API.Controllers;

public record AdminLoginRequest(string Email, string Password);
public record CreateTenantRequest(string Name, string Slug, string Country, decimal MonthlyBillingAmount);
public record UpdateTenantRequest(string? Name, string? Country, decimal? MonthlyBillingAmount, bool? IsActive);
public record CreateTenantUserRequest(string FullName, string Email, string Password, string Role);
public record ChangePasswordRequest(string NewPassword);
public record CreateAgentCategoryRequest(string Name);
public record UpdateAgentCategoryRequest(string? Name, bool? IsActive);
public record AgentTemplateRequest(
    string Name, string Category, string SystemPrompt, bool IsActive = true,
    string? Tone = null, string Language = "es", string? AvatarName = null,
    string? SendFrom = null, string? SendUntil = null,
    int MaxRetries = 3, int RetryIntervalHours = 24, int InactivityCloseHours = 72,
    string? CloseConditionKeyword = null,
    string LlmModel = "claude-sonnet-4-6", double Temperature = 0.3, int MaxTokens = 1024
);
public record MigrateTemplateRequest(Guid TenantId, bool Update = false);
public record AdminCreateWhatsAppLineRequest(string DisplayName, string PhoneNumber, string InstanceId, string ApiToken);
public record AdminUpdateWhatsAppLineRequest(string DisplayName, string PhoneNumber, string? InstanceId, string? ApiToken, bool IsActive);
public record CreateSuperAdminRequest(string FullName, string Email, string Password);
public record UpdateSuperAdminRequest(string? FullName, string? Email, bool? IsActive, string? Password);

[ApiController]
[Route("api/admin")]
public class SuperAdminController(AgentFlowDbContext db, IConfiguration config, IEmailService emailService) : ControllerBase
{
    [HttpPost("login")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Login([FromBody] AdminLoginRequest req, CancellationToken ct)
    {
        try
        {
            var admin = await db.SuperAdmins
                .FirstOrDefaultAsync(a => a.Email == req.Email && a.IsActive, ct);

            if (admin is null)
                return Unauthorized(new { error = "Credenciales invalidas." });

            if (!AuthController.VerifyPassword(req.Password, admin.PasswordHash))
                return Unauthorized(new { error = "Credenciales invalidas." });

            admin.LastLoginAt = DateTime.UtcNow;

            if (admin.MustChangePassword)
            {
                await db.SaveChangesAsync(ct);
                var tempToken = GenerateTempToken(admin.Id.ToString(), "password-change");
                return Ok(new { requiresPasswordChange = true, tempToken });
            }

            var code = AuthController.GenerateOtpCode();
            admin.TwoFactorCode = code;
            admin.TwoFactorExpiry = DateTime.UtcNow.AddMinutes(5);
            await db.SaveChangesAsync(ct);

            _ = emailService.SendTwoFactorCodeAsync(admin.Email, admin.FullName, code, ct);

            var twoFaToken = GenerateTempToken(admin.Id.ToString(), "admin-2fa");
            return Ok(new { requires2FA = true, tempToken = twoFaToken, email = AuthController.MaskEmail(admin.Email) });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ADMIN LOGIN ERROR: {ex}");
            return StatusCode(500, new { error = "Error interno del servidor." });
        }
    }

    [HttpPost("change-password")]
    public async Task<IActionResult> AdminChangePassword([FromBody] ChangePasswordRequest2 req, CancellationToken ct)
    {
        var adminId = ValidateTempTokenLocal(req.TempToken, "password-change");
        if (adminId is null) return Unauthorized(new { error = "Token invalido o expirado." });

        var pwdError = AuthController.ValidatePasswordComplexity(req.NewPassword);
        if (pwdError is not null)
            return BadRequest(new { error = pwdError });

        var admin = await db.SuperAdmins.FindAsync([Guid.Parse(adminId)], ct);
        if (admin is null) return NotFound();

        admin.PasswordHash = AuthController.HashPassword(req.NewPassword);
        admin.MustChangePassword = false;

        var code = AuthController.GenerateOtpCode();
        admin.TwoFactorCode = code;
        admin.TwoFactorExpiry = DateTime.UtcNow.AddMinutes(5);
        await db.SaveChangesAsync(ct);

        _ = emailService.SendTwoFactorCodeAsync(admin.Email, admin.FullName, code, ct);

        var twoFaToken = GenerateTempToken(adminId, "admin-2fa");
        return Ok(new { requires2FA = true, tempToken = twoFaToken, email = AuthController.MaskEmail(admin.Email) });
    }

    [HttpPost("verify-2fa")]
    public async Task<IActionResult> AdminVerify2FA([FromBody] Verify2FARequest req, CancellationToken ct)
    {
        var adminId = ValidateTempTokenLocal(req.TempToken, "admin-2fa");
        if (adminId is null) return Unauthorized(new { error = "Token invalido o expirado." });

        var admin = await db.SuperAdmins.FindAsync([Guid.Parse(adminId)], ct);
        if (admin is null) return NotFound();

        if (admin.TwoFactorCode != req.Code || admin.TwoFactorExpiry < DateTime.UtcNow)
            return Unauthorized(new { error = "Codigo invalido o expirado." });

        admin.TwoFactorCode = null;
        admin.TwoFactorExpiry = null;
        await db.SaveChangesAsync(ct);

        var token = GenerateAdminJwt(admin);
        return Ok(new { token, user = new { id = admin.Id, fullName = admin.FullName, email = admin.Email, role = "super_admin" } });
    }

    [HttpPost("resend-2fa")]
    public async Task<IActionResult> AdminResend2FA([FromBody] Verify2FARequest req, CancellationToken ct)
    {
        var adminId = ValidateTempTokenLocal(req.TempToken, "admin-2fa");
        if (adminId is null) return Unauthorized(new { error = "Token invalido o expirado." });

        var admin = await db.SuperAdmins.FindAsync([Guid.Parse(adminId)], ct);
        if (admin is null) return NotFound();

        var code = AuthController.GenerateOtpCode();
        admin.TwoFactorCode = code;
        admin.TwoFactorExpiry = DateTime.UtcNow.AddMinutes(5);
        await db.SaveChangesAsync(ct);

        _ = emailService.SendTwoFactorCodeAsync(admin.Email, admin.FullName, code, ct);
        return Ok(new { message = "Codigo reenviado." });
    }

    private string? ValidateTempTokenLocal(string token, string purpose)
    {
        try
        {
            var secret = config["Jwt:Secret"] ?? "AgentFlow_Dev_Secret_Key_Min32Chars!!";
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)) { KeyId = "talkia-key" };
            var handler = new JwtSecurityTokenHandler();
            var principal = handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = "agentflow-api",
                ValidateAudience = true,
                ValidAudience = "agentflow-app",
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = key,
                ValidateLifetime = true,
            }, out _);
            var claimedPurpose = principal.FindFirst("purpose")?.Value;
            if (claimedPurpose != purpose) return null;
            return principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        }
        catch { return null; }
    }

    [HttpGet("tenants")]
    [Authorize(Roles = "super_admin")]
    public async Task<IActionResult> GetTenants(CancellationToken ct)
    {
        var tenants = await db.Tenants
            .Select(t => new
            {
                t.Id, t.Name, t.Slug, t.Country, t.MonthlyBillingAmount,
                t.IsActive, t.CreatedAt, t.WhatsAppProvider, t.WhatsAppPhoneNumber,
                UserCount = db.AppUsers.Count(u => u.TenantId == t.Id)
            })
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(ct);

        return Ok(tenants);
    }

    [HttpPost("tenants")]
    [Authorize(Roles = "super_admin")]
    public async Task<IActionResult> CreateTenant([FromBody] CreateTenantRequest req, CancellationToken ct)
    {
        if (await db.Tenants.AnyAsync(t => t.Slug == req.Slug, ct))
            return BadRequest(new { error = "Ya existe un tenant con ese slug." });

        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = req.Name,
            Slug = req.Slug,
            Country = req.Country,
            MonthlyBillingAmount = req.MonthlyBillingAmount,
            IsActive = true,
        };

        db.Tenants.Add(tenant);
        await db.SaveChangesAsync(ct);
        return Ok(new { tenant.Id, tenant.Name, tenant.Slug, tenant.Country, tenant.MonthlyBillingAmount, tenant.IsActive, tenant.CreatedAt });
    }

    [HttpPut("tenants/{id:guid}")]
    [Authorize(Roles = "super_admin")]
    public async Task<IActionResult> UpdateTenant(Guid id, [FromBody] UpdateTenantRequest req, CancellationToken ct)
    {
        var tenant = await db.Tenants.FindAsync([id], ct);
        if (tenant is null) return NotFound();

        if (req.Name is not null) tenant.Name = req.Name;
        if (req.Country is not null) tenant.Country = req.Country;
        if (req.MonthlyBillingAmount.HasValue) tenant.MonthlyBillingAmount = req.MonthlyBillingAmount.Value;

        if (req.IsActive.HasValue)
        {
            tenant.IsActive = req.IsActive.Value;
            if (!req.IsActive.Value)
            {
                await db.AppUsers.Where(u => u.TenantId == id)
                    .ExecuteUpdateAsync(u => u.SetProperty(x => x.IsActive, false), ct);
            }
        }

        await db.SaveChangesAsync(ct);
        return Ok(new { tenant.Id, tenant.Name, tenant.Slug, tenant.Country, tenant.MonthlyBillingAmount, tenant.IsActive });
    }

    // ───── Tenant Configuration (super admin gestiona todos los campos del tenant) ─────

    /// <summary>
    /// Devuelve la configuración completa de un tenant para el super admin —
    /// incluye todos los campos que antes administraba el tenant cliente desde /settings.
    /// API keys se devuelven enmascaradas (sólo últimos 4 chars).
    /// </summary>
    [HttpGet("tenants/{tenantId:guid}/config")]
    [Authorize(Roles = "super_admin")]
    public async Task<IActionResult> GetTenantConfig(Guid tenantId, CancellationToken ct)
    {
        var t = await db.Tenants.FindAsync([tenantId], ct);
        if (t is null) return NotFound();

        static string? Mask(string? s) => string.IsNullOrEmpty(s) ? null : "***" + s[Math.Max(0, s.Length - 4)..];

        return Ok(new
        {
            t.Id, t.Name, t.Slug, t.Country, t.IsActive, t.MonthlyBillingAmount,
            WhatsAppProvider = t.WhatsAppProvider.ToString(),
            t.WhatsAppPhoneNumber,
            BusinessHoursStart = t.BusinessHoursStart.ToString(@"hh\:mm"),
            BusinessHoursEnd = t.BusinessHoursEnd.ToString(@"hh\:mm"),
            t.TimeZone,
            LlmProvider = t.LlmProvider.ToString(),
            LlmApiKey = Mask(t.LlmApiKey),
            t.LlmModel,
            SendGridApiKey = Mask(t.SendGridApiKey),
            t.SenderEmail,
            t.CampaignMessageDelaySeconds,
            t.BrainEnabled,
            t.WebhookContractEnabled,
            t.ReferenceDocumentsEnabled,
            t.MessageBufferSeconds,
        });
    }

    public record UpdateMessageBufferRequest(int Seconds);

    /// <summary>
    /// Configura el debounce de mensajes entrantes. 0 = deshabilitado (procesa cada mensaje al instante).
    /// Rango recomendado: 3–10.
    /// </summary>
    [HttpPut("tenants/{tenantId:guid}/message-buffer")]
    [Authorize(Roles = "super_admin")]
    public async Task<IActionResult> UpdateTenantMessageBuffer(Guid tenantId, [FromBody] UpdateMessageBufferRequest req, CancellationToken ct)
    {
        if (req.Seconds < 0 || req.Seconds > 15)
            return BadRequest(new { error = "Debe estar entre 0 y 15 segundos." });
        var t = await db.Tenants.FindAsync([tenantId], ct);
        if (t is null) return NotFound();
        t.MessageBufferSeconds = req.Seconds;
        await db.SaveChangesAsync(ct);
        return Ok(new { t.MessageBufferSeconds });
    }

    [HttpPut("tenants/{tenantId:guid}/timezone")]
    [Authorize(Roles = "super_admin")]
    public async Task<IActionResult> UpdateTenantTimezone(Guid tenantId, [FromBody] UpdateTimezoneRequest req, CancellationToken ct)
    {
        var t = await db.Tenants.FindAsync([tenantId], ct);
        if (t is null) return NotFound();
        if (string.IsNullOrWhiteSpace(req.TimeZone))
            return BadRequest(new { error = "TimeZone requerido." });
        t.TimeZone = req.TimeZone;
        await db.SaveChangesAsync(ct);
        return Ok(new { t.TimeZone });
    }

    [HttpPut("tenants/{tenantId:guid}/llm")]
    [Authorize(Roles = "super_admin")]
    public async Task<IActionResult> UpdateTenantLlmConfig(Guid tenantId, [FromBody] UpdateLlmConfigRequest req, CancellationToken ct)
    {
        var t = await db.Tenants.FindAsync([tenantId], ct);
        if (t is null) return NotFound();
        if (!Enum.TryParse<AgentFlow.Domain.Enums.LlmProviderType>(req.LlmProvider, true, out var provider))
            return BadRequest(new { error = "Proveedor LLM inválido. Opciones: Anthropic, OpenAI, Gemini." });

        t.LlmProvider = provider;
        t.LlmModel = req.LlmModel;
        // Sólo actualizar API key si viene una real (no la máscara "***xxxx").
        if (!string.IsNullOrEmpty(req.LlmApiKey) && !req.LlmApiKey.StartsWith("***"))
            t.LlmApiKey = req.LlmApiKey;

        await db.SaveChangesAsync(ct);
        return Ok(new { message = "Configuración de LLM actualizada." });
    }

    [HttpPut("tenants/{tenantId:guid}/sendgrid")]
    [Authorize(Roles = "super_admin")]
    public async Task<IActionResult> UpdateTenantSendGridConfig(Guid tenantId, [FromBody] UpdateSendGridRequest req, CancellationToken ct)
    {
        var t = await db.Tenants.FindAsync([tenantId], ct);
        if (t is null) return NotFound();

        if (!string.IsNullOrEmpty(req.SendGridApiKey) && !req.SendGridApiKey.StartsWith("***"))
            t.SendGridApiKey = req.SendGridApiKey;
        if (req.SenderEmail is not null) t.SenderEmail = req.SenderEmail;

        await db.SaveChangesAsync(ct);
        return Ok(new { message = "Configuración de SendGrid actualizada." });
    }

    [HttpPut("tenants/{tenantId:guid}/campaign-delay")]
    [Authorize(Roles = "super_admin")]
    public async Task<IActionResult> UpdateTenantCampaignDelay(Guid tenantId, [FromBody] UpdateCampaignDelayRequest req, CancellationToken ct)
    {
        if (req.DelaySeconds < 3 || req.DelaySeconds > 120)
            return BadRequest(new { error = "El delay debe estar entre 3 y 120 segundos." });
        var t = await db.Tenants.FindAsync([tenantId], ct);
        if (t is null) return NotFound();
        t.CampaignMessageDelaySeconds = req.DelaySeconds;
        await db.SaveChangesAsync(ct);
        return Ok(new { t.CampaignMessageDelaySeconds });
    }

    public record UpdateTenantFlagRequest(bool Enabled);

    [HttpPut("tenants/{tenantId:guid}/brain")]
    [Authorize(Roles = "super_admin")]
    public async Task<IActionResult> UpdateTenantBrainEnabled(Guid tenantId, [FromBody] UpdateTenantFlagRequest req, CancellationToken ct)
    {
        var t = await db.Tenants.FindAsync([tenantId], ct);
        if (t is null) return NotFound();

        if (req.Enabled)
        {
            var hasWelcome = await db.AgentRegistryEntries
                .AnyAsync(r => r.TenantId == tenantId && r.IsWelcome && r.IsActive, ct);
            if (!hasWelcome)
                return BadRequest(new { error = "Debes registrar un Agente Welcome antes de activar el Cerebro." });
        }

        t.BrainEnabled = req.Enabled;
        await db.SaveChangesAsync(ct);
        return Ok(new { t.BrainEnabled });
    }

    [HttpPut("tenants/{tenantId:guid}/webhook-contract")]
    [Authorize(Roles = "super_admin")]
    public async Task<IActionResult> UpdateTenantWebhookContract(Guid tenantId, [FromBody] UpdateTenantFlagRequest req, CancellationToken ct)
    {
        var t = await db.Tenants.FindAsync([tenantId], ct);
        if (t is null) return NotFound();
        t.WebhookContractEnabled = req.Enabled;
        await db.SaveChangesAsync(ct);
        return Ok(new { t.WebhookContractEnabled });
    }

    [HttpPut("tenants/{tenantId:guid}/reference-documents")]
    [Authorize(Roles = "super_admin")]
    public async Task<IActionResult> UpdateTenantReferenceDocuments(Guid tenantId, [FromBody] UpdateTenantFlagRequest req, CancellationToken ct)
    {
        var t = await db.Tenants.FindAsync([tenantId], ct);
        if (t is null) return NotFound();
        t.ReferenceDocumentsEnabled = req.Enabled;
        await db.SaveChangesAsync(ct);
        return Ok(new { t.ReferenceDocumentsEnabled });
    }

    [HttpGet("tenants/{tenantId:guid}/agents")]
    [Authorize(Roles = "super_admin")]
    public async Task<IActionResult> GetTenantAgents(Guid tenantId, CancellationToken ct)
    {
        var agents = await db.AgentDefinitions
            .Where(a => a.TenantId == tenantId && a.IsActive)
            .OrderBy(a => a.Name)
            .Select(a => new { a.Id, a.Name })
            .ToListAsync(ct);
        return Ok(agents);
    }

    /// <summary>
    /// Devuelve las asignaciones del tenant (prompts globales asignados + acciones
    /// per-tenant con su flag de asignación). Usado por el modal "Asignaciones" del admin panel.
    /// </summary>
    [HttpGet("tenants/{tenantId:guid}/assignments")]
    [Authorize(Roles = "super_admin")]
    public async Task<IActionResult> GetTenantAssignments(Guid tenantId, CancellationToken ct)
    {
        var tenant = await db.Tenants.FindAsync([tenantId], ct);
        if (tenant is null) return NotFound();

        // Catálogo completo visible en el tab: globales + todas las acciones legacy
        // (de cualquier tenant). El admin puede asignar cualquiera a este tenant.
        // Se incluye OriginTenantName para que el admin vea de dónde viene cada una.
        var actions = await (
            from a in db.ActionDefinitions
            join t in db.Tenants on a.TenantId equals t.Id into gt
            from t in gt.DefaultIfEmpty()
            orderby a.TenantId == null ? 0 : 1, a.Name
            select new
            {
                a.Id, a.Name, a.Description, a.IsActive, a.RequiresWebhook, a.SendsEmail, a.SendsSms,
                IsGlobal = a.TenantId == null,
                OriginTenantId = a.TenantId,
                OriginTenantName = t != null ? t.Name : null
            }).ToListAsync(ct);

        // Filtrar assignedActionIds a solo los IDs que aún existen en el catálogo.
        // Evita que el frontend inicialice checkboxes con GUIDs de acciones eliminadas
        // y luego falle al guardar con 400.
        var storedActionIds = tenant.AssignedActionIds ?? [];
        var validActionIds = storedActionIds.Count > 0
            ? await db.ActionDefinitions
                .Where(a => storedActionIds.Contains(a.Id))
                .Select(a => a.Id)
                .ToListAsync(ct)
            : new List<Guid>();

        return Ok(new
        {
            assignedPromptIds = tenant.AssignedPromptIds,
            assignedActionIds = validActionIds,
            actions
        });
    }

    public record AssignPromptsRequest(List<Guid> PromptIds);
    public record AssignActionsRequest(List<Guid> ActionIds);

    /// <summary>
    /// Reemplaza la lista de PromptTemplates asignados al tenant. Si la lista
    /// queda vacía el tenant vuelve a ver todos los prompts activos (retrocompat).
    /// Si al desasignar quedan prompts en uso por maestros de campaña del tenant,
    /// devuelve 409 con la lista de maestros bloqueantes.
    /// </summary>
    [HttpPut("tenants/{tenantId:guid}/assigned-prompts")]
    [Authorize(Roles = "super_admin")]
    public async Task<IActionResult> SetAssignedPrompts(Guid tenantId, [FromBody] AssignPromptsRequest req, CancellationToken ct)
    {
        var tenant = await db.Tenants.FindAsync([tenantId], ct);
        if (tenant is null) return NotFound();

        var ids = req.PromptIds?.Distinct().ToList() ?? [];

        // Validar que los IDs correspondan a prompts que existen
        if (ids.Count > 0)
        {
            var existingCount = await db.Set<PromptTemplate>()
                .Where(p => ids.Contains(p.Id))
                .CountAsync(ct);
            if (existingCount != ids.Count)
                return BadRequest(new { error = "Uno o más PromptTemplates no existen." });
        }

        // Detectar prompts que se están desasignando y que actualmente están
        // en uso por algún maestro de campaña del tenant. Se valida en todos los casos
        // (incluido "limpiar todo") porque el filtro del cliente es estricto.
        var currentSet = tenant.AssignedPromptIds.ToHashSet();
        var newSet = ids.ToHashSet();
        var removedIds = currentSet.Where(id => !newSet.Contains(id)).ToList();
        if (removedIds.Count > 0)
        {
            var conflicts = await FindPromptConflictsAsync(tenantId, removedIds, ct);
            if (conflicts.Count > 0)
            {
                return Conflict(new
                {
                    error = "No es posible desasignar prompts en uso. El cliente debe removerlos primero en su maestro de campaña.",
                    conflicts
                });
            }
        }

        tenant.AssignedPromptIds = ids;
        await db.SaveChangesAsync(ct);

        return Ok(new { assignedPromptIds = tenant.AssignedPromptIds });
    }

    /// <summary>
    /// Reemplaza la lista de ActionDefinitions asignadas al tenant. Si la lista
    /// queda vacía el tenant vuelve a ver todas sus acciones activas (retrocompat).
    /// Si al desasignar quedan acciones en uso por maestros de campaña del tenant,
    /// devuelve 409 con la lista de maestros bloqueantes.
    /// </summary>
    [HttpPut("tenants/{tenantId:guid}/assigned-actions")]
    [Authorize(Roles = "super_admin")]
    public async Task<IActionResult> SetAssignedActions(Guid tenantId, [FromBody] AssignActionsRequest req, CancellationToken ct)
    {
        var tenant = await db.Tenants.FindAsync([tenantId], ct);
        if (tenant is null) return NotFound();

        var rawIds = req.ActionIds?.Distinct().ToList() ?? [];

        // Filtrar a solo IDs que realmente existen en el catálogo.
        // Usar ids válidos en lugar de retornar 400 — evita bloqueos por acciones
        // eliminadas que quedaron en AssignedActionIds del tenant.
        var ids = rawIds.Count > 0
            ? await db.ActionDefinitions
                .Where(a => rawIds.Contains(a.Id))
                .Select(a => a.Id)
                .ToListAsync(ct)
            : new List<Guid>();

        var currentSet = tenant.AssignedActionIds.ToHashSet();
        var newSet = ids.ToHashSet();
        var removedIds = currentSet.Where(id => !newSet.Contains(id)).ToList();
        if (removedIds.Count > 0)
        {
            // Solo verificar conflictos para acciones que aún existen en el catálogo.
            // Los IDs que ya no existen en ActionDefinitions son referencias obsoletas
            // (basura de acciones eliminadas) — se limpian silenciosamente sin 409.
            var existingRemovedIds = await db.ActionDefinitions
                .Where(a => removedIds.Contains(a.Id))
                .Select(a => a.Id)
                .ToListAsync(ct);

            if (existingRemovedIds.Count > 0)
            {
                var conflicts = await FindActionConflictsAsync(tenantId, existingRemovedIds, ct);
                if (conflicts.Count > 0)
                {
                    return Conflict(new
                    {
                        error = "No es posible desasignar acciones en uso. El cliente debe removerlas primero en su maestro de campaña.",
                        conflicts
                    });
                }
            }
        }

        tenant.AssignedActionIds = ids;
        await db.SaveChangesAsync(ct);

        return Ok(new { assignedActionIds = tenant.AssignedActionIds });
    }

    /// <summary>
    /// Busca maestros de campaña del tenant que referencien cualquiera de los promptIds dados.
    /// EF no puede traducir Contains sobre List&lt;Guid&gt; en JSON; traemos los maestros (pocos
    /// por tenant) y filtramos en memoria.
    /// </summary>
    private async Task<List<object>> FindPromptConflictsAsync(Guid tenantId, List<Guid> promptIds, CancellationToken ct)
    {
        var removed = promptIds.ToHashSet();
        var templates = await db.CampaignTemplates
            .Where(t => t.TenantId == tenantId)
            .Select(t => new { t.Id, t.Name, t.PromptTemplateIds, t.IsActive })
            .ToListAsync(ct);

        var conflicts = new List<object>();
        foreach (var t in templates)
        {
            var used = t.PromptTemplateIds.Where(id => removed.Contains(id)).ToList();
            if (used.Count > 0)
                conflicts.Add(new { templateId = t.Id, templateName = t.Name, usedIds = used, isActive = t.IsActive });
        }
        return conflicts;
    }

    private async Task<List<object>> FindActionConflictsAsync(Guid tenantId, List<Guid> actionIds, CancellationToken ct)
    {
        var removed = actionIds.ToHashSet();
        var templates = await db.CampaignTemplates
            .Where(t => t.TenantId == tenantId)
            .Select(t => new { t.Id, t.Name, t.ActionIds, t.IsActive })
            .ToListAsync(ct);

        var conflicts = new List<object>();
        foreach (var t in templates)
        {
            var used = t.ActionIds.Where(id => removed.Contains(id)).ToList();
            if (used.Count > 0)
                conflicts.Add(new { templateId = t.Id, templateName = t.Name, usedIds = used, isActive = t.IsActive });
        }
        return conflicts;
    }

    [HttpGet("tenants/{tenantId:guid}/users")]
    [Authorize(Roles = "super_admin")]
    public async Task<IActionResult> GetTenantUsers(Guid tenantId, CancellationToken ct)
    {
        var users = await db.AppUsers
            .Where(u => u.TenantId == tenantId)
            .Select(u => new { u.Id, u.FullName, u.Email, Role = u.Role.ToString(), u.IsActive, u.CreatedAt, u.LastLoginAt })
            .ToListAsync(ct);

        return Ok(users);
    }

    [HttpPost("tenants/{tenantId:guid}/users")]
    [Authorize(Roles = "super_admin")]
    public async Task<IActionResult> CreateTenantUser(Guid tenantId, [FromBody] CreateTenantUserRequest req, CancellationToken ct)
    {
        if (!await db.Tenants.AnyAsync(t => t.Id == tenantId, ct))
            return NotFound(new { error = "Tenant no encontrado." });

        if (await db.AppUsers.AnyAsync(u => u.TenantId == tenantId && u.Email == req.Email, ct))
            return BadRequest(new { error = "Ya existe un usuario con ese email en este tenant." });

        if (!Enum.TryParse<UserRole>(req.Role, true, out var role))
            return BadRequest(new { error = "Rol invalido." });

        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            FullName = req.FullName,
            Email = req.Email,
            PasswordHash = AuthController.HashPassword(req.Password),
            Role = role,
            IsActive = true,
        };

        db.AppUsers.Add(user);
        await db.SaveChangesAsync(ct);

        var tenantName = await db.Tenants.Where(t => t.Id == tenantId).Select(t => t.Name).FirstOrDefaultAsync(ct) ?? "";
        _ = emailService.SendWelcomeTenantEmailAsync(req.Email, req.FullName, req.Password, tenantName, ct);

        return Ok(new { user.Id, user.FullName, user.Email, Role = user.Role.ToString(), user.IsActive });
    }

    [HttpPut("tenants/{tenantId:guid}/users/{userId:guid}/password")]
    [Authorize(Roles = "super_admin")]
    public async Task<IActionResult> ChangeUserPassword(Guid tenantId, Guid userId, [FromBody] ChangePasswordRequest req, CancellationToken ct)
    {
        var user = await db.AppUsers.FirstOrDefaultAsync(u => u.Id == userId && u.TenantId == tenantId, ct);
        if (user is null) return NotFound();

        user.PasswordHash = AuthController.HashPassword(req.NewPassword);
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "Contrasena actualizada." });
    }

    // ── Super Admin Users ─────────────────────────────────

    [HttpGet("users")]
    [Authorize(Roles = "super_admin")]
    public async Task<IActionResult> GetAdminUsers(CancellationToken ct)
    {
        var admins = await db.SuperAdmins
            .OrderBy(a => a.FullName)
            .Select(a => new { a.Id, a.FullName, a.Email, a.IsActive, a.CreatedAt, a.LastLoginAt })
            .ToListAsync(ct);
        return Ok(admins);
    }

    [HttpPost("users")]
    [Authorize(Roles = "super_admin")]
    public async Task<IActionResult> CreateAdminUser([FromBody] CreateSuperAdminRequest req, CancellationToken ct)
    {
        if (req.Password.Length < 8)
            return BadRequest(new { error = "La contrasena debe tener al menos 8 caracteres." });

        if (await db.SuperAdmins.AnyAsync(a => a.Email == req.Email, ct))
            return BadRequest(new { error = "Ya existe un administrador con ese email." });

        var admin = new SuperAdmin
        {
            Id = Guid.NewGuid(),
            FullName = req.FullName,
            Email = req.Email,
            PasswordHash = AuthController.HashPassword(req.Password),
            IsActive = true,
        };

        db.SuperAdmins.Add(admin);
        await db.SaveChangesAsync(ct);

        _ = emailService.SendWelcomeAdminEmailAsync(req.Email, req.FullName, req.Password, ct);

        return Ok(new { admin.Id, admin.FullName, admin.Email, admin.IsActive, admin.CreatedAt });
    }

    [HttpPut("users/{id:guid}")]
    [Authorize(Roles = "super_admin")]
    public async Task<IActionResult> UpdateAdminUser(Guid id, [FromBody] UpdateSuperAdminRequest req, CancellationToken ct)
    {
        var admin = await db.SuperAdmins.FindAsync([id], ct);
        if (admin is null) return NotFound();

        if (req.FullName is not null) admin.FullName = req.FullName;
        if (req.Email is not null)
        {
            if (await db.SuperAdmins.AnyAsync(a => a.Email == req.Email && a.Id != id, ct))
                return BadRequest(new { error = "Ya existe un administrador con ese email." });
            admin.Email = req.Email;
        }
        if (req.IsActive.HasValue) admin.IsActive = req.IsActive.Value;
        if (req.Password is not null)
        {
            if (req.Password.Length < 8)
                return BadRequest(new { error = "La contrasena debe tener al menos 8 caracteres." });
            admin.PasswordHash = AuthController.HashPassword(req.Password);
        }

        await db.SaveChangesAsync(ct);
        return Ok(new { admin.Id, admin.FullName, admin.Email, admin.IsActive });
    }

    // ── Agent Categories ───────────────────────────────────

    [HttpGet("agent-categories")]
    [Authorize(Roles = "super_admin")]
    public async Task<IActionResult> GetAgentCategories(CancellationToken ct)
    {
        var categories = await db.AgentCategories
            .OrderBy(c => c.Name)
            .Select(c => new { c.Id, c.Name, c.IsActive, c.CreatedAt })
            .ToListAsync(ct);

        return Ok(categories);
    }

    [HttpPost("agent-categories")]
    [Authorize(Roles = "super_admin")]
    public async Task<IActionResult> CreateAgentCategory([FromBody] CreateAgentCategoryRequest req, CancellationToken ct)
    {
        if (await db.AgentCategories.AnyAsync(c => c.Name == req.Name, ct))
            return BadRequest(new { error = "Ya existe una categoria con ese nombre." });

        var category = new AgentCategory { Id = Guid.NewGuid(), Name = req.Name };
        db.AgentCategories.Add(category);
        await db.SaveChangesAsync(ct);
        return Ok(new { category.Id, category.Name, category.IsActive, category.CreatedAt });
    }

    [HttpPut("agent-categories/{id:guid}")]
    [Authorize(Roles = "super_admin")]
    public async Task<IActionResult> UpdateAgentCategory(Guid id, [FromBody] UpdateAgentCategoryRequest req, CancellationToken ct)
    {
        var category = await db.AgentCategories.FindAsync([id], ct);
        if (category is null) return NotFound();

        if (req.Name is not null)
        {
            if (await db.AgentCategories.AnyAsync(c => c.Name == req.Name && c.Id != id, ct))
                return BadRequest(new { error = "Ya existe una categoria con ese nombre." });
            category.Name = req.Name;
        }
        if (req.IsActive.HasValue) category.IsActive = req.IsActive.Value;

        await db.SaveChangesAsync(ct);
        return Ok(new { category.Id, category.Name, category.IsActive });
    }

    [HttpDelete("agent-categories/{id:guid}")]
    [Authorize(Roles = "super_admin")]
    public async Task<IActionResult> DeleteAgentCategory(Guid id, CancellationToken ct)
    {
        var category = await db.AgentCategories.FindAsync([id], ct);
        if (category is null) return NotFound();

        db.AgentCategories.Remove(category);
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "Categoria eliminada." });
    }

    // ── Agent Templates ─────────────────────────────────────

    [HttpGet("agent-templates")]
    [Authorize(Roles = "super_admin")]
    public async Task<IActionResult> GetAgentTemplates(CancellationToken ct)
    {
        var templates = await db.AgentTemplates
            .OrderBy(t => t.Category).ThenBy(t => t.Name)
            .Select(t => new { t.Id, t.Name, t.Category, t.SystemPrompt, t.IsActive, t.CreatedAt, t.UpdatedAt })
            .ToListAsync(ct);

        return Ok(templates);
    }

    [HttpGet("agent-templates/{id:guid}")]
    [Authorize(Roles = "super_admin")]
    public async Task<IActionResult> GetAgentTemplate(Guid id, CancellationToken ct)
    {
        var t = await db.AgentTemplates.FindAsync([id], ct);
        if (t is null) return NotFound();
        return Ok(new
        {
            t.Id, t.Name, t.Category, t.IsActive,
            t.SystemPrompt, t.Tone, t.Language, t.AvatarName,
            t.SendFrom, t.SendUntil, t.MaxRetries, t.RetryIntervalHours,
            t.InactivityCloseHours, t.CloseConditionKeyword,
            t.LlmModel, t.Temperature, t.MaxTokens,
            t.CreatedAt, t.UpdatedAt
        });
    }

    [HttpPost("agent-templates")]
    [Authorize(Roles = "super_admin")]
    public async Task<IActionResult> CreateAgentTemplate([FromBody] AgentTemplateRequest req, CancellationToken ct)
    {
        var template = new AgentTemplate
        {
            Id = Guid.NewGuid(),
            Name = req.Name, Category = req.Category, IsActive = req.IsActive,
            SystemPrompt = req.SystemPrompt, Tone = req.Tone, Language = req.Language, AvatarName = req.AvatarName,
            SendFrom = req.SendFrom, SendUntil = req.SendUntil,
            MaxRetries = req.MaxRetries, RetryIntervalHours = req.RetryIntervalHours,
            InactivityCloseHours = req.InactivityCloseHours, CloseConditionKeyword = req.CloseConditionKeyword,
            LlmModel = req.LlmModel, Temperature = req.Temperature, MaxTokens = req.MaxTokens,
        };

        db.AgentTemplates.Add(template);
        await db.SaveChangesAsync(ct);
        return Ok(new { template.Id, template.Name });
    }

    [HttpPut("agent-templates/{id:guid}")]
    [Authorize(Roles = "super_admin")]
    public async Task<IActionResult> UpdateAgentTemplate(Guid id, [FromBody] AgentTemplateRequest req, CancellationToken ct)
    {
        var template = await db.AgentTemplates.FindAsync([id], ct);
        if (template is null) return NotFound();

        template.Name = req.Name; template.Category = req.Category; template.IsActive = req.IsActive;
        template.SystemPrompt = req.SystemPrompt; template.Tone = req.Tone; template.Language = req.Language; template.AvatarName = req.AvatarName;
        template.SendFrom = req.SendFrom; template.SendUntil = req.SendUntil;
        template.MaxRetries = req.MaxRetries; template.RetryIntervalHours = req.RetryIntervalHours;
        template.InactivityCloseHours = req.InactivityCloseHours; template.CloseConditionKeyword = req.CloseConditionKeyword;
        template.LlmModel = req.LlmModel; template.Temperature = req.Temperature; template.MaxTokens = req.MaxTokens;
        template.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        return Ok(new { template.Id, template.Name });
    }

    [HttpDelete("agent-templates/{id:guid}")]
    [Authorize(Roles = "super_admin")]
    public async Task<IActionResult> DeleteAgentTemplate(Guid id, CancellationToken ct)
    {
        var template = await db.AgentTemplates.FindAsync([id], ct);
        if (template is null) return NotFound();

        db.AgentTemplates.Remove(template);
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "Template eliminado." });
    }

    [HttpPost("agent-templates/{id:guid}/migrate")]
    [Authorize(Roles = "super_admin")]
    public async Task<IActionResult> MigrateTemplate(Guid id, [FromBody] MigrateTemplateRequest req, CancellationToken ct)
    {
        var template = await db.AgentTemplates.FindAsync([id], ct);
        if (template is null) return NotFound(new { error = "Template no encontrado." });

        if (!await db.Tenants.AnyAsync(t => t.Id == req.TenantId && t.IsActive, ct))
            return NotFound(new { error = "Tenant no encontrado o inactivo." });

        if (req.Update)
        {
            // Buscar agente existente creado por esta plantilla en ese tenant
            var existing = await db.AgentDefinitions
                .FirstOrDefaultAsync(a => a.TenantId == req.TenantId && a.SourceTemplateId == id, ct);

            if (existing is null)
                return BadRequest(new { error = "No se encontro un agente creado por esta plantilla en ese tenant. Usa 'Migrar' primero." });

            existing.Name = template.Name;
            existing.SystemPrompt = template.SystemPrompt;
            existing.Tone = template.Tone;
            existing.Language = template.Language;
            existing.AvatarName = template.AvatarName;
            existing.SendFrom = template.SendFrom != null ? TimeOnly.Parse(template.SendFrom) : null;
            existing.SendUntil = template.SendUntil != null ? TimeOnly.Parse(template.SendUntil) : null;
            existing.MaxRetries = template.MaxRetries;
            existing.RetryIntervalHours = template.RetryIntervalHours;
            existing.InactivityCloseHours = template.InactivityCloseHours;
            existing.CloseConditionKeyword = template.CloseConditionKeyword;
            existing.LlmModel = template.LlmModel;
            existing.Temperature = template.Temperature;
            existing.MaxTokens = template.MaxTokens;
            existing.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync(ct);
            return Ok(new { agentId = existing.Id, existing.Name, tenantId = req.TenantId, action = "updated" });
        }
        else
        {
            var agent = new AgentDefinition
            {
                Id = Guid.NewGuid(),
                TenantId = req.TenantId,
                Name = template.Name,
                Type = AgentFlow.Domain.Enums.AgentType.General,
                SystemPrompt = template.SystemPrompt,
                Tone = template.Tone,
                Language = template.Language,
                AvatarName = template.AvatarName,
                SendFrom = template.SendFrom != null ? TimeOnly.Parse(template.SendFrom) : null,
                SendUntil = template.SendUntil != null ? TimeOnly.Parse(template.SendUntil) : null,
                MaxRetries = template.MaxRetries,
                RetryIntervalHours = template.RetryIntervalHours,
                InactivityCloseHours = template.InactivityCloseHours,
                CloseConditionKeyword = template.CloseConditionKeyword,
                LlmModel = template.LlmModel,
                Temperature = template.Temperature,
                MaxTokens = template.MaxTokens,
                SourceTemplateId = id,
                IsActive = true,
            };

            db.AgentDefinitions.Add(agent);
            await db.SaveChangesAsync(ct);
            return Ok(new { agentId = agent.Id, agent.Name, tenantId = req.TenantId, action = "created" });
        }
    }

    // ── WhatsApp Lines (Admin) ─────────────────────────────

    [HttpGet("whatsapp-lines")]
    [Authorize(Roles = "super_admin")]
    public async Task<IActionResult> GetAllWhatsAppLines(CancellationToken ct)
    {
        var lines = await db.WhatsAppLines
            .Where(l => l.TenantId == null)
            .OrderBy(l => l.CreatedAt)
            .Select(l => new
            {
                l.Id, l.DisplayName, l.PhoneNumber, l.InstanceId,
                Provider = l.Provider.ToString(), l.IsActive, l.CreatedAt,
            })
            .ToListAsync(ct);
        return Ok(lines);
    }

    [HttpPost("whatsapp-lines")]
    [Authorize(Roles = "super_admin")]
    public async Task<IActionResult> CreateWhatsAppLine([FromBody] AdminCreateWhatsAppLineRequest req, CancellationToken ct)
    {
        var line = new WhatsAppLine
        {
            Id = Guid.NewGuid(),
            TenantId = null,
            DisplayName = req.DisplayName,
            PhoneNumber = req.PhoneNumber,
            InstanceId = req.InstanceId,
            ApiToken = req.ApiToken,
            Provider = AgentFlow.Domain.Enums.ProviderType.UltraMsg,
            IsActive = true,
        };

        db.WhatsAppLines.Add(line);
        await db.SaveChangesAsync(ct);
        return Ok(new { line.Id, line.DisplayName, line.PhoneNumber, line.InstanceId });
    }

    [HttpPut("whatsapp-lines/{id:guid}")]
    [Authorize(Roles = "super_admin")]
    public async Task<IActionResult> UpdateWhatsAppLine(Guid id, [FromBody] AdminUpdateWhatsAppLineRequest req, CancellationToken ct)
    {
        var line = await db.WhatsAppLines.FindAsync([id], ct);
        if (line is null) return NotFound();

        line.DisplayName = req.DisplayName;
        line.PhoneNumber = req.PhoneNumber;
        line.IsActive = req.IsActive;

        if (!string.IsNullOrWhiteSpace(req.InstanceId))
            line.InstanceId = req.InstanceId;

        if (!string.IsNullOrWhiteSpace(req.ApiToken))
            line.ApiToken = req.ApiToken;

        line.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return Ok(new
        {
            line.Id, line.DisplayName, line.PhoneNumber, line.InstanceId,
            Provider = line.Provider.ToString(), line.IsActive, line.CreatedAt, line.UpdatedAt,
        });
    }

    [HttpDelete("whatsapp-lines/{id:guid}")]
    [Authorize(Roles = "super_admin")]
    public async Task<IActionResult> DeleteWhatsAppLine(Guid id, CancellationToken ct)
    {
        var line = await db.WhatsAppLines.FindAsync([id], ct);
        if (line is null) return NotFound();
        db.WhatsAppLines.Remove(line);
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "Linea eliminada." });
    }

    [HttpGet("whatsapp-lines/{id:guid}/status")]
    [Authorize(Roles = "super_admin")]
    public async Task<IActionResult> GetWhatsAppLineStatus(Guid id, [FromServices] IUltraMsgInstanceService ultraMsg, CancellationToken ct)
    {
        var line = await db.WhatsAppLines.FindAsync([id], ct);
        if (line is null) return NotFound();

        try
        {
            var status = await ultraMsg.GetStatusAsync(line.InstanceId, line.ApiToken, ct);
            if (!string.IsNullOrEmpty(status.Phone) && status.Phone != line.PhoneNumber)
            {
                line.PhoneNumber = status.Phone;
                line.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
            }
            return Ok(new { status = status.Status, phone = line.PhoneNumber, instanceId = line.InstanceId, lineId = line.Id, displayName = line.DisplayName });
        }
        catch (HttpRequestException)
        {
            return StatusCode(502, new { error = "Error al conectar con UltraMsg." });
        }
    }

    [HttpGet("whatsapp-lines/{id:guid}/qr")]
    [Authorize(Roles = "super_admin")]
    public async Task<IActionResult> GetWhatsAppLineQr(Guid id, [FromServices] IUltraMsgInstanceService ultraMsg, CancellationToken ct)
    {
        var line = await db.WhatsAppLines.FindAsync([id], ct);
        if (line is null) return NotFound();

        try
        {
            var qrBytes = await ultraMsg.GetQrCodeAsync(line.InstanceId, line.ApiToken, ct);
            return File(qrBytes, "image/png");
        }
        catch (InvalidOperationException) { return BadRequest(new { error = "QR no disponible para esta instancia." }); }
        catch (HttpRequestException) { return StatusCode(502, new { error = "Error al obtener QR." }); }
    }

    [HttpPost("whatsapp-lines/{id:guid}/restart")]
    [Authorize(Roles = "super_admin")]
    public async Task<IActionResult> RestartWhatsAppLine(Guid id, [FromServices] IUltraMsgInstanceService ultraMsg, CancellationToken ct)
    {
        var line = await db.WhatsAppLines.FindAsync([id], ct);
        if (line is null) return NotFound();

        try
        {
            var success = await ultraMsg.RestartAsync(line.InstanceId, line.ApiToken, ct);
            return success ? Ok(new { message = "Reiniciada." }) : StatusCode(502, new { error = "No se pudo reiniciar." });
        }
        catch (HttpRequestException) { return StatusCode(502, new { error = "Error al reiniciar la instancia." }); }
    }

    [HttpPost("whatsapp-lines/{id:guid}/logout")]
    [Authorize(Roles = "super_admin")]
    public async Task<IActionResult> LogoutWhatsAppLine(Guid id, [FromServices] IUltraMsgInstanceService ultraMsg, CancellationToken ct)
    {
        var line = await db.WhatsAppLines.FindAsync([id], ct);
        if (line is null) return NotFound();

        try
        {
            var success = await ultraMsg.LogoutAsync(line.InstanceId, line.ApiToken, ct);
            return success ? Ok(new { message = "Sesion cerrada." }) : StatusCode(502, new { error = "No se pudo cerrar sesion." });
        }
        catch (HttpRequestException) { return StatusCode(502, new { error = "Error al cerrar sesion." }); }
    }

    // ───── Action Definitions ─────

    public record ActionDefinitionRequest(
        string Name, string? Description,
        bool RequiresWebhook, bool SendsEmail, bool SendsSms,
        string? WebhookUrl = null, string? WebhookMethod = null,
        string? DefaultTriggerConfig = null,
        string? DefaultWebhookContract = null,
        Guid? TenantId = null,
        bool IsProcess = false,
        bool IsDelinquencyDownload = false);

    /// <summary>
    /// Lista acciones. Sin parámetros devuelve todo el catálogo (globales + legacy per-tenant).
    /// scope=global filtra sólo globales; tenantId filtra una única acción legacy.
    /// </summary>
    [HttpGet("actions")]
    [Authorize(Roles = "super_admin")]
    public async Task<IActionResult> GetActions([FromQuery] Guid? tenantId, [FromQuery] string? scope, CancellationToken ct)
    {
        var query = db.ActionDefinitions.AsQueryable();
        if (scope == "global")
            query = query.Where(a => a.TenantId == null);
        else if (tenantId.HasValue)
            query = query.Where(a => a.TenantId == tenantId.Value);

        var actions = await query
            .OrderBy(a => a.Name)
            .Select(a => new { a.Id, a.TenantId, a.Name, a.Description, a.RequiresWebhook, a.SendsEmail, a.SendsSms, a.IsProcess, a.IsDelinquencyDownload, a.WebhookUrl, a.WebhookMethod, a.IsActive, a.CreatedAt, a.DefaultTriggerConfig, a.DefaultWebhookContract })
            .ToListAsync(ct);
        return Ok(actions);
    }

    [HttpPost("actions")]
    [Authorize(Roles = "super_admin")]
    public async Task<IActionResult> CreateAction([FromBody] ActionDefinitionRequest req, CancellationToken ct)
    {
        if (req.TenantId.HasValue && !await db.Tenants.AnyAsync(t => t.Id == req.TenantId.Value, ct))
            return BadRequest(new { error = "Tenant no encontrado." });

        // Unicidad de nombre: entre globales, o dentro del mismo tenant (si es legacy).
        var duplicate = req.TenantId.HasValue
            ? await db.ActionDefinitions.AnyAsync(a => a.TenantId == req.TenantId.Value && a.Name == req.Name, ct)
            : await db.ActionDefinitions.AnyAsync(a => a.TenantId == null && a.Name == req.Name, ct);

        if (duplicate)
            return BadRequest(new { error = "Ya existe una accion con ese nombre en el catalogo." });

        var action = new ActionDefinition
        {
            Id = Guid.NewGuid(),
            TenantId = req.TenantId,
            Name = req.Name,
            Description = req.Description,
            RequiresWebhook = req.RequiresWebhook,
            SendsEmail = req.SendsEmail,
            SendsSms = req.SendsSms,
            IsProcess = req.IsProcess,
            IsDelinquencyDownload = req.IsDelinquencyDownload,
            WebhookUrl = req.WebhookUrl,
            WebhookMethod = req.WebhookMethod,
            DefaultTriggerConfig = req.DefaultTriggerConfig,
            DefaultWebhookContract = req.DefaultWebhookContract,
        };
        db.ActionDefinitions.Add(action);
        await db.SaveChangesAsync(ct);
        return Ok(new { action.Id, action.Name });
    }

    [HttpPut("actions/{id:guid}")]
    [Authorize(Roles = "super_admin")]
    public async Task<IActionResult> UpdateAction(Guid id, [FromBody] ActionDefinitionRequest req, CancellationToken ct)
    {
        var action = await db.ActionDefinitions.FindAsync([id], ct);
        if (action is null) return NotFound();

        // Unicidad: el scope se preserva (TenantId no se cambia al editar).
        var duplicate = action.TenantId.HasValue
            ? await db.ActionDefinitions.AnyAsync(a => a.TenantId == action.TenantId && a.Name == req.Name && a.Id != id, ct)
            : await db.ActionDefinitions.AnyAsync(a => a.TenantId == null && a.Name == req.Name && a.Id != id, ct);

        if (duplicate)
            return BadRequest(new { error = "Ya existe una accion con ese nombre en el catalogo." });

        action.Name = req.Name;
        action.Description = req.Description;
        action.RequiresWebhook = req.RequiresWebhook;
        action.SendsEmail = req.SendsEmail;
        action.SendsSms = req.SendsSms;
        action.IsProcess = req.IsProcess;
        action.IsDelinquencyDownload = req.IsDelinquencyDownload;
        action.WebhookUrl = req.WebhookUrl;
        action.WebhookMethod = req.WebhookMethod;
        action.DefaultTriggerConfig = req.DefaultTriggerConfig;
        action.DefaultWebhookContract = req.DefaultWebhookContract;
        action.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(new { action.Id, action.Name });
    }

    [HttpPut("actions/{id:guid}/toggle")]
    [Authorize(Roles = "super_admin")]
    public async Task<IActionResult> ToggleAction(Guid id, CancellationToken ct)
    {
        var action = await db.ActionDefinitions.FindAsync([id], ct);
        if (action is null) return NotFound();
        action.IsActive = !action.IsActive;
        action.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(new { action.Id, action.IsActive });
    }

    [HttpDelete("actions/{id:guid}")]
    [Authorize(Roles = "super_admin")]
    public async Task<IActionResult> DeleteAction(Guid id, CancellationToken ct)
    {
        var action = await db.ActionDefinitions.FindAsync([id], ct);
        if (action is null) return NotFound();
        db.ActionDefinitions.Remove(action);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Lista las acciones asignadas a un tenant con su estado de configuración webhook.
    /// Equivalente al endpoint /api/tenant-actions pero accesible por super-admin
    /// para administrar el contract desde el modal de edición de tenant.
    /// </summary>
    [HttpGet("tenants/{tenantId:guid}/actions-config")]
    [Authorize(Roles = "super_admin")]
    public async Task<IActionResult> GetTenantActionsConfig(Guid tenantId, CancellationToken ct)
    {
        var tenant = await db.Tenants
            .Where(t => t.Id == tenantId)
            .Select(t => new { t.AssignedActionIds })
            .FirstOrDefaultAsync(ct);
        if (tenant is null) return NotFound();

        var assignedIds = tenant.AssignedActionIds ?? [];
        if (assignedIds.Count == 0) return Ok(Array.Empty<object>());

        var actions = await db.ActionDefinitions
            .Where(a => a.IsActive && a.RequiresWebhook && assignedIds.Contains(a.Id))
            .OrderBy(a => a.Name)
            .Select(a => new
            {
                a.Id, a.Name, a.Description,
                a.RequiresWebhook, a.SendsEmail, a.SendsSms, a.IsProcess, a.IsDelinquencyDownload,
                a.DefaultWebhookContract, a.DefaultTriggerConfig,
                HasWebhookContract = a.DefaultWebhookContract != null
            })
            .ToListAsync(ct);

        // Para acciones marcadas como descarga de morosidad, verificar si el tenant tiene
        // un ActionDelinquencyConfig con DownloadWebhookUrl configurado.
        // Esas acciones no usan el WebhookBuilder — su endpoint vive en Admin → Morosidad.
        var actionIds = actions.Select(a => a.Id).ToList();
        var delinquencyLookup = await db.ActionDelinquencyConfigs
            .Where(c => c.TenantId == tenantId && actionIds.Contains(c.ActionDefinitionId))
            .Select(c => new { c.ActionDefinitionId, HasUrl = c.DownloadWebhookUrl != null && c.DownloadWebhookUrl != "" })
            .ToListAsync(ct);
        var dlLookup = delinquencyLookup.ToDictionary(c => c.ActionDefinitionId, c => c.HasUrl);

        var result = actions.Select(a => new
        {
            a.Id, a.Name, a.Description,
            a.RequiresWebhook, a.SendsEmail, a.SendsSms, a.IsProcess,
            a.DefaultWebhookContract, a.DefaultTriggerConfig,
            HasWebhookContract    = a.DefaultWebhookContract != null,
            IsDelinquencyAction   = a.IsDelinquencyDownload,
            HasDelinquencyConfig  = dlLookup.TryGetValue(a.Id, out var has) && has
        }).ToList();

        return Ok(result);
    }

    public record AdminUpdateActionContractRequest(string? Contract);

    /// <summary>
    /// Prompts del proceso de etiquetado del tenant.
    /// </summary>
    [HttpGet("tenants/{tenantId:guid}/labeling-prompts")]
    [Authorize(Roles = "super_admin")]
    public async Task<IActionResult> GetTenantLabelingPrompts(Guid tenantId, CancellationToken ct)
    {
        var t = await db.Tenants
            .Where(t => t.Id == tenantId)
            .Select(t => new { t.LabelingAnalysisPrompt, t.LabelingResultSchemaPrompt })
            .FirstOrDefaultAsync(ct);
        if (t is null) return NotFound();
        return Ok(new
        {
            analysisPrompt = t.LabelingAnalysisPrompt,
            resultSchemaPrompt = t.LabelingResultSchemaPrompt
        });
    }

    public record UpdateLabelingPromptsRequest(string? AnalysisPrompt, string? ResultSchemaPrompt);

    [HttpPut("tenants/{tenantId:guid}/labeling-prompts")]
    [Authorize(Roles = "super_admin")]
    public async Task<IActionResult> UpdateTenantLabelingPrompts(
        Guid tenantId, [FromBody] UpdateLabelingPromptsRequest req, CancellationToken ct)
    {
        var tenant = await db.Tenants.FindAsync([tenantId], ct);
        if (tenant is null) return NotFound();
        tenant.LabelingAnalysisPrompt = string.IsNullOrWhiteSpace(req.AnalysisPrompt) ? null : req.AnalysisPrompt;
        tenant.LabelingResultSchemaPrompt = string.IsNullOrWhiteSpace(req.ResultSchemaPrompt) ? null : req.ResultSchemaPrompt;
        await db.SaveChangesAsync(ct);
        return Ok(new
        {
            analysisPrompt = tenant.LabelingAnalysisPrompt,
            resultSchemaPrompt = tenant.LabelingResultSchemaPrompt
        });
    }

    /// <summary>
    /// Actualiza el DefaultWebhookContract de una acción asignada a un tenant.
    /// Mismo comportamiento que /api/tenant-actions/{id}/webhook-contract pero
    /// invocable desde el panel admin (modal de edición de tenant).
    /// </summary>
    [HttpPut("tenants/{tenantId:guid}/actions/{actionId:guid}/webhook-contract")]
    [Authorize(Roles = "super_admin")]
    public async Task<IActionResult> UpdateTenantActionContract(
        Guid tenantId, Guid actionId,
        [FromBody] AdminUpdateActionContractRequest req,
        CancellationToken ct)
    {
        var tenant = await db.Tenants
            .Where(t => t.Id == tenantId)
            .Select(t => new { t.AssignedActionIds })
            .FirstOrDefaultAsync(ct);
        if (tenant is null) return NotFound();

        var assignedIds = tenant.AssignedActionIds ?? [];
        if (!assignedIds.Contains(actionId))
            return NotFound(new { error = "Acción no asignada a este tenant." });

        var action = await db.ActionDefinitions.FirstOrDefaultAsync(a => a.Id == actionId, ct);
        if (action is null)
            return NotFound(new { error = "Acción no encontrada." });

        action.DefaultWebhookContract = string.IsNullOrWhiteSpace(req.Contract) ? null : req.Contract;

        // Sincronizar DefaultTriggerConfig si el contract trae triggerConfig.
        if (!string.IsNullOrWhiteSpace(req.Contract))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(req.Contract);
                if (doc.RootElement.TryGetProperty("triggerConfig", out var tc)
                    && tc.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    action.DefaultTriggerConfig = tc.GetRawText();
                }
            }
            catch { /* contract inválido — no sincronizar */ }
        }

        action.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return Ok(new { action.Id, action.DefaultWebhookContract, action.DefaultTriggerConfig });
    }

    // ───── Prompt Templates ─────

    public record PromptTemplateRequest(string Name, string? Description, Guid? CategoryId, string? SystemPrompt, string? ResultPrompt, string? AnalysisPrompts, string? FieldMapping);

    [HttpGet("prompts")]
    [Authorize(Roles = "super_admin")]
    public async Task<IActionResult> GetPrompts(CancellationToken ct)
    {
        var prompts = await db.PromptTemplates
            .Include(p => p.Category)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new
            {
                p.Id, p.Name, p.Description,
                CategoryId = p.CategoryId,
                CategoryName = p.Category != null ? p.Category.Name : null,
                p.SystemPrompt, p.ResultPrompt, p.AnalysisPrompts, p.FieldMapping,
                p.IsActive, p.CreatedAt
            })
            .ToListAsync(ct);
        return Ok(prompts);
    }

    [HttpGet("prompts/{id:guid}")]
    [Authorize(Roles = "super_admin")]
    public async Task<IActionResult> GetPrompt(Guid id, CancellationToken ct)
    {
        var p = await db.PromptTemplates.Include(x => x.Category).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return NotFound();
        return Ok(new
        {
            p.Id, p.Name, p.Description,
            CategoryId = p.CategoryId,
            CategoryName = p.Category?.Name,
            p.SystemPrompt, p.ResultPrompt, p.AnalysisPrompts, p.FieldMapping,
            p.IsActive, p.CreatedAt
        });
    }

    [HttpPost("prompts")]
    [Authorize(Roles = "super_admin")]
    public async Task<IActionResult> CreatePrompt([FromBody] PromptTemplateRequest req, CancellationToken ct)
    {
        var prompt = new PromptTemplate
        {
            Id = Guid.NewGuid(),
            Name = req.Name,
            Description = req.Description,
            CategoryId = req.CategoryId,
            SystemPrompt = req.SystemPrompt,
            ResultPrompt = req.ResultPrompt,
            AnalysisPrompts = req.AnalysisPrompts,
            FieldMapping = req.FieldMapping,
        };
        db.PromptTemplates.Add(prompt);
        await db.SaveChangesAsync(ct);
        return Ok(new { prompt.Id, prompt.Name });
    }

    [HttpPut("prompts/{id:guid}")]
    [Authorize(Roles = "super_admin")]
    public async Task<IActionResult> UpdatePrompt(Guid id, [FromBody] PromptTemplateRequest req, CancellationToken ct)
    {
        var prompt = await db.PromptTemplates.FindAsync([id], ct);
        if (prompt is null) return NotFound();
        prompt.Name = req.Name;
        prompt.Description = req.Description;
        prompt.CategoryId = req.CategoryId;
        prompt.SystemPrompt = req.SystemPrompt;
        prompt.ResultPrompt = req.ResultPrompt;
        prompt.AnalysisPrompts = req.AnalysisPrompts;
        prompt.FieldMapping = req.FieldMapping;
        prompt.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(new { prompt.Id, prompt.Name });
    }

    [HttpPut("prompts/{id:guid}/toggle")]
    [Authorize(Roles = "super_admin")]
    public async Task<IActionResult> TogglePrompt(Guid id, CancellationToken ct)
    {
        var prompt = await db.PromptTemplates.FindAsync([id], ct);
        if (prompt is null) return NotFound();
        prompt.IsActive = !prompt.IsActive;
        prompt.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(new { prompt.Id, prompt.IsActive });
    }

    [HttpDelete("prompts/{id:guid}")]
    [Authorize(Roles = "super_admin")]
    public async Task<IActionResult> DeletePrompt(Guid id, CancellationToken ct)
    {
        var prompt = await db.PromptTemplates.FindAsync([id], ct);
        if (prompt is null) return NotFound();
        db.PromptTemplates.Remove(prompt);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private string GenerateTempToken(string userId, string purpose)
    {
        var secret = config["Jwt:Secret"] ?? "AgentFlow_Dev_Secret_Key_Min32Chars!!";
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)) { KeyId = "talkia-key" };
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId),
            new Claim("purpose", purpose),
        };
        var token = new JwtSecurityToken(issuer: "agentflow-api", audience: "agentflow-app",
            claims: claims, expires: DateTime.UtcNow.AddMinutes(15), signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private string GenerateAdminJwt(SuperAdmin admin)
    {
        var secret = config["Jwt:Secret"] ?? "AgentFlow_Dev_Secret_Key_Min32Chars!!";
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)) { KeyId = "talkia-key" };
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, admin.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, admin.Email),
            new Claim(ClaimTypes.Role, "super_admin"),
            new Claim("full_name", admin.FullName),
        };

        var token = new JwtSecurityToken(
            issuer: "agentflow-api",
            audience: "agentflow-app",
            claims: claims,
            expires: DateTime.UtcNow.AddHours(12),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    // ───── Agent Registry (Cerebro) ─────

    public record AgentRegistryRequest(
        Guid TenantId, string Slug, string Name, string Capabilities,
        Guid CampaignTemplateId, bool IsWelcome = false);

    [HttpGet("agent-registry")]
    [Authorize(Roles = "super_admin")]
    public async Task<IActionResult> GetAgentRegistry([FromQuery] Guid? tenantId, CancellationToken ct)
    {
        var query = db.AgentRegistryEntries.AsQueryable();
        if (tenantId.HasValue)
            query = query.Where(r => r.TenantId == tenantId.Value);

        var entries = await query
            .OrderBy(r => r.Name)
            .Select(r => new
            {
                r.Id, r.TenantId, r.Slug, r.Name, r.Capabilities,
                r.CampaignTemplateId,
                CampaignTemplateName = db.CampaignTemplates.Where(ct2 => ct2.Id == r.CampaignTemplateId).Select(ct2 => ct2.Name).FirstOrDefault(),
                r.IsWelcome, r.IsActive, r.CreatedAt
            })
            .ToListAsync(ct);
        return Ok(entries);
    }

    [HttpPost("agent-registry")]
    [Authorize(Roles = "super_admin")]
    public async Task<IActionResult> CreateAgentRegistryEntry([FromBody] AgentRegistryRequest req, CancellationToken ct)
    {
        if (!await db.Tenants.AnyAsync(t => t.Id == req.TenantId, ct))
            return BadRequest(new { error = "Tenant no encontrado." });

        if (await db.AgentRegistryEntries.AnyAsync(r => r.TenantId == req.TenantId && r.Slug == req.Slug, ct))
            return BadRequest(new { error = "Ya existe un agente con ese slug para este tenant." });

        if (!await db.CampaignTemplates.AnyAsync(t => t.Id == req.CampaignTemplateId, ct))
            return BadRequest(new { error = "Maestro de campaña no encontrado." });

        if (req.IsWelcome && await db.AgentRegistryEntries.AnyAsync(r => r.TenantId == req.TenantId && r.IsWelcome, ct))
            return BadRequest(new { error = "Ya existe un agente welcome para este tenant. Desactive el actual primero." });

        var entry = new AgentRegistryEntry
        {
            Id = Guid.NewGuid(),
            TenantId = req.TenantId,
            Slug = req.Slug,
            Name = req.Name,
            Capabilities = req.Capabilities,
            CampaignTemplateId = req.CampaignTemplateId,
            IsWelcome = req.IsWelcome,
        };
        db.AgentRegistryEntries.Add(entry);
        await db.SaveChangesAsync(ct);
        return Ok(new { entry.Id, entry.Slug, entry.Name });
    }

    [HttpPut("agent-registry/{id:guid}")]
    [Authorize(Roles = "super_admin")]
    public async Task<IActionResult> UpdateAgentRegistryEntry(Guid id, [FromBody] AgentRegistryRequest req, CancellationToken ct)
    {
        var entry = await db.AgentRegistryEntries.FindAsync([id], ct);
        if (entry is null) return NotFound();

        if (await db.AgentRegistryEntries.AnyAsync(r => r.TenantId == entry.TenantId && r.Slug == req.Slug && r.Id != id, ct))
            return BadRequest(new { error = "Ya existe un agente con ese slug para este tenant." });

        if (req.IsWelcome && !entry.IsWelcome
            && await db.AgentRegistryEntries.AnyAsync(r => r.TenantId == entry.TenantId && r.IsWelcome && r.Id != id, ct))
            return BadRequest(new { error = "Ya existe un agente welcome para este tenant." });

        entry.Slug = req.Slug;
        entry.Name = req.Name;
        entry.Capabilities = req.Capabilities;
        entry.CampaignTemplateId = req.CampaignTemplateId;
        entry.IsWelcome = req.IsWelcome;
        entry.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(new { entry.Id, entry.Slug, entry.Name });
    }

    [HttpPut("agent-registry/{id:guid}/toggle")]
    [Authorize(Roles = "super_admin")]
    public async Task<IActionResult> ToggleAgentRegistryEntry(Guid id, CancellationToken ct)
    {
        var entry = await db.AgentRegistryEntries.FindAsync([id], ct);
        if (entry is null) return NotFound();
        entry.IsActive = !entry.IsActive;
        entry.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(new { entry.Id, entry.IsActive });
    }

    [HttpDelete("agent-registry/{id:guid}")]
    [Authorize(Roles = "super_admin")]
    public async Task<IActionResult> DeleteAgentRegistryEntry(Guid id, CancellationToken ct)
    {
        var entry = await db.AgentRegistryEntries.FindAsync([id], ct);
        if (entry is null) return NotFound();
        db.AgentRegistryEntries.Remove(entry);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}

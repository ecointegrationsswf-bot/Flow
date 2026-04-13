using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using AgentFlow.Domain.Entities;
using AgentFlow.Infrastructure.Email;
using AgentFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace AgentFlow.API.Controllers;

public record LoginRequest(string Email, string Password);
public record UpdateSendGridRequest(string? SendGridApiKey, string? SenderEmail);
public record UpdateLlmConfigRequest(string LlmProvider, string? LlmApiKey, string LlmModel);
public record UpdateTimezoneRequest(string TimeZone);
public record UpdateCampaignDelayRequest(int DelaySeconds);
public record ForgotPasswordRequest(string Email);
public record ResetPasswordRequest(string Token, string NewPassword);
public record Verify2FARequest(string TempToken, string Code);
public record ChangePasswordRequest2(string TempToken, string NewPassword);

public record LoginResponse(
    string Token,
    string TenantId,
    UserInfo User
);

public record UserInfo(
    string Id,
    string FullName,
    string Email,
    string Role,
    string? AvatarUrl = null,
    List<string>? Permissions = null
);

[ApiController]
[Route("api/auth")]
public class AuthController(AgentFlowDbContext db, IConfiguration config, IEmailService emailService) : ControllerBase
{
    [HttpPost("login")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req, CancellationToken ct)
    {
        try
        {
            var user = await db.AppUsers
                .Include(u => u.Tenant)
                .FirstOrDefaultAsync(u => u.Email == req.Email && u.IsActive, ct);

            if (user is null)
                return Unauthorized(new { error = "Credenciales invalidas." });

            if (!VerifyPassword(req.Password, user.PasswordHash))
                return Unauthorized(new { error = "Credenciales invalidas." });

            user.LastLoginAt = DateTime.UtcNow;

            // Paso 1: Si debe cambiar contraseña
            if (user.MustChangePassword)
            {
                await db.SaveChangesAsync(ct);
                var tempToken = GenerateTempToken(user.Id.ToString(), "password-change");
                return Ok(new { requiresPasswordChange = true, tempToken });
            }

            // Paso 2: Enviar código 2FA
            var code = GenerateOtpCode();
            user.TwoFactorCode = code;
            user.TwoFactorExpiry = DateTime.UtcNow.AddMinutes(5);
            await db.SaveChangesAsync(ct);

            _ = emailService.SendTwoFactorCodeAsync(user.Email, user.FullName, code, ct);

            var twoFaToken = GenerateTempToken(user.Id.ToString(), "2fa");
            return Ok(new { requires2FA = true, tempToken = twoFaToken, email = MaskEmail(user.Email) });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"LOGIN ERROR: {ex}");
            return StatusCode(500, new { error = "Error interno del servidor." });
        }
    }

    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest2 req, CancellationToken ct)
    {
        var userId = ValidateTempToken(req.TempToken, "password-change");
        if (userId is null) return Unauthorized(new { error = "Token invalido o expirado." });

        var pwdError = ValidatePasswordComplexity(req.NewPassword);
        if (pwdError is not null)
            return BadRequest(new { error = pwdError });

        var user = await db.AppUsers.FindAsync([Guid.Parse(userId)], ct);
        if (user is null) return NotFound();

        user.PasswordHash = HashPassword(req.NewPassword);
        user.MustChangePassword = false;

        // Generar 2FA
        var code = GenerateOtpCode();
        user.TwoFactorCode = code;
        user.TwoFactorExpiry = DateTime.UtcNow.AddMinutes(5);
        await db.SaveChangesAsync(ct);

        _ = emailService.SendTwoFactorCodeAsync(user.Email, user.FullName, code, ct);

        var twoFaToken = GenerateTempToken(userId, "2fa");
        return Ok(new { requires2FA = true, tempToken = twoFaToken, email = MaskEmail(user.Email) });
    }

    [HttpPost("verify-2fa")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Verify2FA([FromBody] Verify2FARequest req, CancellationToken ct)
    {
        var userId = ValidateTempToken(req.TempToken, "2fa");
        if (userId is null) return Unauthorized(new { error = "Token invalido o expirado." });

        var user = await db.AppUsers
            .Include(u => u.Tenant)
            .FirstOrDefaultAsync(u => u.Id == Guid.Parse(userId), ct);

        if (user is null) return NotFound();

        if (user.TwoFactorCode != req.Code || user.TwoFactorExpiry < DateTime.UtcNow)
            return Unauthorized(new { error = "Codigo invalido o expirado." });

        // Limpiar 2FA
        user.TwoFactorCode = null;
        user.TwoFactorExpiry = null;
        await db.SaveChangesAsync(ct);

        var token = GenerateJwt(user);
        return Ok(new LoginResponse(
            Token: token,
            TenantId: user.TenantId.ToString(),
            User: new UserInfo(
                Id: user.Id.ToString(),
                FullName: user.FullName,
                Email: user.Email,
                Role: user.Role.ToString(),
                AvatarUrl: user.AvatarUrl,
                Permissions: user.Permissions ?? []
            )
        ));
    }

    [HttpPost("resend-2fa")]
    public async Task<IActionResult> Resend2FA([FromBody] Verify2FARequest req, CancellationToken ct)
    {
        var userId = ValidateTempToken(req.TempToken, "2fa");
        if (userId is null) return Unauthorized(new { error = "Token invalido o expirado." });

        var user = await db.AppUsers.FindAsync([Guid.Parse(userId)], ct);
        if (user is null) return NotFound();

        var code = GenerateOtpCode();
        user.TwoFactorCode = code;
        user.TwoFactorExpiry = DateTime.UtcNow.AddMinutes(5);
        await db.SaveChangesAsync(ct);

        _ = emailService.SendTwoFactorCodeAsync(user.Email, user.FullName, code, ct);

        return Ok(new { message = "Codigo reenviado." });
    }

    private string GenerateJwt(AppUser user)
    {
        var secret = config["Jwt:Secret"] ?? "AgentFlow_Dev_Secret_Key_Min32Chars!!";
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)) { KeyId = "talkia-key" };
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim("tenant_id", user.TenantId.ToString()),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
            new Claim("full_name", user.FullName),
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

    [HttpGet("tenant")]
    [Microsoft.AspNetCore.Authorization.Authorize]
    public async Task<IActionResult> GetMyTenant(CancellationToken ct)
    {
        var tenantIdStr = User.FindFirst("tenant_id")?.Value;
        if (tenantIdStr is null || !Guid.TryParse(tenantIdStr, out var tenantId))
            return Unauthorized();

        var tenant = await db.Tenants
            .Where(t => t.Id == tenantId)
            .Select(t => new
            {
                t.Id, t.Name, t.Slug, t.Country,
                WhatsAppProvider = t.WhatsAppProvider.ToString(),
                t.WhatsAppPhoneNumber,
                BusinessHoursStart = t.BusinessHoursStart.ToString("HH:mm"),
                BusinessHoursEnd = t.BusinessHoursEnd.ToString("HH:mm"),
                t.TimeZone, t.IsActive, t.MonthlyBillingAmount,
                LlmProvider = t.LlmProvider.ToString(),
                LlmApiKey = string.IsNullOrEmpty(t.LlmApiKey) ? null : "***" + t.LlmApiKey.Substring(Math.Max(0, t.LlmApiKey.Length - 4)),
                t.LlmModel,
                SendGridApiKey = string.IsNullOrEmpty(t.SendGridApiKey) ? null : "***" + t.SendGridApiKey.Substring(Math.Max(0, t.SendGridApiKey.Length - 4)),
                t.SenderEmail,
                t.CampaignMessageDelaySeconds,
                t.BrainEnabled,
                t.WebhookContractEnabled
            })
            .FirstOrDefaultAsync(ct);

        if (tenant is null) return NotFound();
        return Ok(tenant);
    }

    [HttpPut("tenant/sendgrid")]
    [Microsoft.AspNetCore.Authorization.Authorize]
    public async Task<IActionResult> UpdateTenantSendGrid([FromBody] UpdateSendGridRequest req, CancellationToken ct)
    {
        var tenantIdStr = User.FindFirst("tenant_id")?.Value;
        if (tenantIdStr is null || !Guid.TryParse(tenantIdStr, out var tenantId))
            return Unauthorized();

        var tenant = await db.Tenants.FindAsync([tenantId], ct);
        if (tenant is null) return NotFound();

        tenant.SendGridApiKey = req.SendGridApiKey;
        tenant.SenderEmail = req.SenderEmail;
        await db.SaveChangesAsync(ct);

        return Ok(new { message = "Configuracion de SendGrid actualizada." });
    }

    [HttpPut("tenant/timezone")]
    [Microsoft.AspNetCore.Authorization.Authorize]
    public async Task<IActionResult> UpdateTenantTimezone([FromBody] UpdateTimezoneRequest req, CancellationToken ct)
    {
        var tenantIdStr = User.FindFirst("tenant_id")?.Value;
        if (tenantIdStr is null || !Guid.TryParse(tenantIdStr, out var tenantId))
            return Unauthorized();

        var tenant = await db.Tenants.FindAsync([tenantId], ct);
        if (tenant is null) return NotFound();

        tenant.TimeZone = req.TimeZone;
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "Zona horaria actualizada." });
    }

    [HttpPut("tenant/llm")]
    [Microsoft.AspNetCore.Authorization.Authorize]
    public async Task<IActionResult> UpdateTenantLlm([FromBody] UpdateLlmConfigRequest req, CancellationToken ct)
    {
        var tenantIdStr = User.FindFirst("tenant_id")?.Value;
        if (tenantIdStr is null || !Guid.TryParse(tenantIdStr, out var tenantId))
            return Unauthorized();

        var tenant = await db.Tenants.FindAsync([tenantId], ct);
        if (tenant is null) return NotFound();

        if (!Enum.TryParse<AgentFlow.Domain.Enums.LlmProviderType>(req.LlmProvider, true, out var provider))
            return BadRequest(new { error = "Proveedor LLM invalido. Opciones: Anthropic, OpenAI, Gemini." });

        tenant.LlmProvider = provider;
        tenant.LlmModel = req.LlmModel;

        // Solo actualizar API key si el usuario envió una nueva (no la máscara "***xxxx")
        if (!string.IsNullOrEmpty(req.LlmApiKey) && !req.LlmApiKey.StartsWith("***"))
            tenant.LlmApiKey = req.LlmApiKey;

        await db.SaveChangesAsync(ct);
        return Ok(new { message = "Configuracion de LLM actualizada." });
    }

    [HttpPut("tenant/campaign-delay")]
    [Microsoft.AspNetCore.Authorization.Authorize]
    public async Task<IActionResult> UpdateCampaignDelay([FromBody] UpdateCampaignDelayRequest req, CancellationToken ct)
    {
        if (req.DelaySeconds < 3 || req.DelaySeconds > 120)
            return BadRequest(new { error = "El delay debe estar entre 3 y 120 segundos." });

        var tenantIdStr = User.FindFirst("tenant_id")?.Value;
        if (tenantIdStr is null || !Guid.TryParse(tenantIdStr, out var tenantId))
            return Unauthorized();

        var tenant = await db.Tenants.FindAsync([tenantId], ct);
        if (tenant is null) return NotFound();

        tenant.CampaignMessageDelaySeconds = req.DelaySeconds;
        await db.SaveChangesAsync(ct);

        return Ok(new { delaySeconds = tenant.CampaignMessageDelaySeconds });
    }

    [HttpPut("tenant/brain")]
    [Microsoft.AspNetCore.Authorization.Authorize]
    public async Task<IActionResult> UpdateBrainEnabled([FromBody] UpdateBrainEnabledRequest req, CancellationToken ct)
    {
        var tenantIdStr = User.FindFirst("tenant_id")?.Value;
        if (tenantIdStr is null || !Guid.TryParse(tenantIdStr, out var tenantId))
            return Unauthorized();

        var tenant = await db.Tenants.FindAsync([tenantId], ct);
        if (tenant is null) return NotFound();

        // Si se intenta activar, verificar que existe un Agente Welcome
        if (req.BrainEnabled)
        {
            var hasWelcome = await db.AgentRegistryEntries
                .AnyAsync(r => r.TenantId == tenantId && r.IsWelcome && r.IsActive, ct);
            if (!hasWelcome)
                return BadRequest(new { error = "Debes registrar un Agente Welcome antes de activar el Cerebro. Ve a Cerebro → Registro de agentes." });
        }

        tenant.BrainEnabled = req.BrainEnabled;
        await db.SaveChangesAsync(ct);
        return Ok(new { tenant.BrainEnabled });
    }

    public record UpdateBrainEnabledRequest(bool BrainEnabled);

    [HttpPut("tenant/webhook-contract")]
    [Microsoft.AspNetCore.Authorization.Authorize]
    public async Task<IActionResult> UpdateWebhookContractEnabled([FromBody] UpdateWebhookContractRequest req, CancellationToken ct)
    {
        var tenantIdStr = User.FindFirst("tenant_id")?.Value;
        if (tenantIdStr is null || !Guid.TryParse(tenantIdStr, out var tenantId))
            return Unauthorized();

        var tenant = await db.Tenants.FindAsync([tenantId], ct);
        if (tenant is null) return NotFound();

        tenant.WebhookContractEnabled = req.Enabled;
        await db.SaveChangesAsync(ct);
        return Ok(new { tenant.WebhookContractEnabled });
    }

    public record UpdateWebhookContractRequest(bool Enabled);

    [HttpPost("forgot-password")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest req, CancellationToken ct)
    {
        // Siempre responder OK para no revelar si el email existe
        var user = await db.AppUsers.FirstOrDefaultAsync(u => u.Email == req.Email && u.IsActive, ct);
        if (user is not null)
        {
            var resetToken = GenerateTempToken(user.Id.ToString(), "password-reset");
            _ = emailService.SendPasswordResetAsync(user.Email, user.FullName, resetToken, ct);
        }
        return Ok(new { message = "Si el email existe, recibiras un enlace para restablecer tu contrasena." });
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest req, CancellationToken ct)
    {
        var userId = ValidateTempToken(req.Token, "password-reset");
        if (userId is null) return BadRequest(new { error = "El enlace es invalido o ha expirado." });

        var pwdError = ValidatePasswordComplexity(req.NewPassword);
        if (pwdError is not null)
            return BadRequest(new { error = pwdError });

        var user = await db.AppUsers.FindAsync([Guid.Parse(userId)], ct);
        if (user is null) return NotFound();

        user.PasswordHash = HashPassword(req.NewPassword);
        user.MustChangePassword = false;
        await db.SaveChangesAsync(ct);

        return Ok(new { message = "Contrasena actualizada. Ya puedes iniciar sesion." });
    }

    // ── Helpers ──────────────────────────────────────────────

    internal static string? ValidatePasswordComplexity(string password)
    {
        if (password.Length < 8) return "La contrasena debe tener al menos 8 caracteres.";
        if (!password.Any(char.IsUpper)) return "La contrasena debe tener al menos una mayuscula.";
        if (!password.Any(char.IsLower)) return "La contrasena debe tener al menos una minuscula.";
        if (!password.Any(char.IsDigit)) return "La contrasena debe tener al menos un numero.";
        return null;
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

        var token = new JwtSecurityToken(
            issuer: "agentflow-api",
            audience: "agentflow-app",
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    internal string? ValidateTempToken(string token, string expectedPurpose)
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
            var purpose = principal.FindFirst("purpose")?.Value;
            if (purpose != expectedPurpose) return null;
            return principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        }
        catch { return null; }
    }

    internal static string GenerateOtpCode()
    {
        return RandomNumberGenerator.GetInt32(100000, 999999).ToString();
    }

    internal static string MaskEmail(string email)
    {
        var parts = email.Split('@');
        if (parts.Length != 2 || parts[0].Length < 3) return email;
        return parts[0][..2] + new string('*', parts[0].Length - 2) + "@" + parts[1];
    }

    // ── Password hashing con PBKDF2 ────────────────────────
    internal static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt, 100_000, HashAlgorithmName.SHA256, 32);

        var result = new byte[48]; // 16 salt + 32 hash
        Buffer.BlockCopy(salt, 0, result, 0, 16);
        Buffer.BlockCopy(hash, 0, result, 16, 32);
        return Convert.ToBase64String(result);
    }

    internal static bool VerifyPassword(string password, string storedHash)
    {
        var decoded = Convert.FromBase64String(storedHash);
        if (decoded.Length != 48) return false;

        var salt = decoded[..16];
        var storedHashBytes = decoded[16..];

        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt, 100_000, HashAlgorithmName.SHA256, 32);

        return CryptographicOperations.FixedTimeEquals(hash, storedHashBytes);
    }

    // Retorna el usuario autenticado con permisos frescos desde la BD
    [HttpGet("me")]
    [Microsoft.AspNetCore.Authorization.Authorize]
    public async Task<IActionResult> GetMe(CancellationToken ct)
    {
        var userId = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userId is null || !Guid.TryParse(userId, out var id))
            return Unauthorized();

        var user = await db.AppUsers
            .Where(u => u.Id == id)
            .Select(u => new
            {
                Id = u.Id.ToString(),
                u.FullName,
                u.Email,
                Role = u.Role.ToString(),
                u.AvatarUrl,
                u.Permissions,
            })
            .FirstOrDefaultAsync(ct);

        if (user is null) return NotFound();

        return Ok(new UserInfo(
            Id: user.Id,
            FullName: user.FullName,
            Email: user.Email,
            Role: user.Role,
            AvatarUrl: user.AvatarUrl,
            Permissions: user.Permissions ?? []
        ));
    }

    // Endpoint ligero para mantener el app pool activo (keep-alive)
    [HttpGet("ping")]
    [Microsoft.AspNetCore.Authorization.AllowAnonymous]
    public IActionResult Ping() => Ok(new { status = "ok", ts = DateTime.UtcNow });
}

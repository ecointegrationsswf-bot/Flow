using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using AgentFlow.Domain.Entities;
using AgentFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace AgentFlow.API.Controllers;

public record LoginRequest(string Email, string Password);

public record LoginResponse(
    string Token,
    string TenantId,
    UserInfo User
);

public record UserInfo(
    string Id,
    string FullName,
    string Email,
    string Role
);

[ApiController]
[Route("api/auth")]
public class AuthController(AgentFlowDbContext db, IConfiguration config) : ControllerBase
{
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req, CancellationToken ct)
    {
        var user = await db.AppUsers
            .Include(u => u.Tenant)
            .FirstOrDefaultAsync(u => u.Email == req.Email && u.IsActive, ct);

        if (user is null)
            return Unauthorized(new { error = "Credenciales invalidas." });

        if (!VerifyPassword(req.Password, user.PasswordHash))
            return Unauthorized(new { error = "Credenciales invalidas." });

        user.LastLoginAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        var token = GenerateJwt(user);

        return Ok(new LoginResponse(
            Token: token,
            TenantId: user.TenantId.ToString(),
            User: new UserInfo(
                Id: user.Id.ToString(),
                FullName: user.FullName,
                Email: user.Email,
                Role: user.Role.ToString()
            )
        ));
    }

    private string GenerateJwt(AppUser user)
    {
        var secret = config["Jwt:Secret"] ?? "AgentFlow_Dev_Secret_Key_Min32Chars!!";
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
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
}

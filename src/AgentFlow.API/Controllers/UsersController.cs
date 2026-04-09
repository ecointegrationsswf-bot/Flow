using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Interfaces;
using AgentFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgentFlow.API.Controllers;

public record CreateUserRequest(string FullName, string Email, string Password, string Role, bool CanEditPhone = false, List<Guid>? AllowedAgentIds = null, List<string>? Permissions = null);
public record UpdateUserRequest(string FullName, string Email, string Role, bool IsActive, bool CanEditPhone = false, List<Guid>? AllowedAgentIds = null, string? Password = null, List<string>? Permissions = null);

[ApiController]
[Route("api/users")]
public class UsersController(AgentFlowDbContext db, ITenantContext tenantCtx) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var tenantId = tenantCtx.TenantId;

        var users = await db.AppUsers
            .Where(u => u.TenantId == tenantId)
            .OrderBy(u => u.FullName)
            .Select(u => new
            {
                u.Id,
                u.TenantId,
                u.FullName,
                u.Email,
                Role = u.Role.ToString(),
                u.IsActive,
                u.CanEditPhone,
                u.AllowedAgentIds,
                u.Permissions,
                u.CreatedAt,
                u.LastLoginAt,
            })
            .ToListAsync(ct);

        return Ok(users);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var tenantId = tenantCtx.TenantId;

        var user = await db.AppUsers
            .Where(u => u.Id == id && u.TenantId == tenantId)
            .Select(u => new
            {
                u.Id,
                u.TenantId,
                u.FullName,
                u.Email,
                Role = u.Role.ToString(),
                u.IsActive,
                u.CanEditPhone,
                u.AllowedAgentIds,
                u.Permissions,
                u.CreatedAt,
                u.LastLoginAt,
            })
            .FirstOrDefaultAsync(ct);

        if (user is null)
            return NotFound(new { error = "Usuario no encontrado." });

        return Ok(user);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest req, CancellationToken ct)
    {
        var tenantId = tenantCtx.TenantId;

        // Validar email unico
        var exists = await db.AppUsers
            .AnyAsync(u => u.Email == req.Email && u.TenantId == tenantId, ct);
        if (exists)
            return Conflict(new { error = "Ya existe un usuario con ese email." });

        if (!Enum.TryParse<UserRole>(req.Role, out var role))
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
            CanEditPhone = req.CanEditPhone,
            AllowedAgentIds = req.AllowedAgentIds ?? [],
            Permissions = req.Permissions ?? [],
            CreatedAt = DateTime.UtcNow,
        };

        db.AppUsers.Add(user);
        await db.SaveChangesAsync(ct);

        return Ok(new
        {
            user.Id,
            user.TenantId,
            user.FullName,
            user.Email,
            Role = user.Role.ToString(),
            user.IsActive,
            user.CreatedAt,
            user.LastLoginAt,
        });
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateUserRequest req, CancellationToken ct)
    {
        var tenantId = tenantCtx.TenantId;

        var user = await db.AppUsers
            .FirstOrDefaultAsync(u => u.Id == id && u.TenantId == tenantId, ct);

        if (user is null)
            return NotFound(new { error = "Usuario no encontrado." });

        // Validar email unico si cambio
        if (user.Email != req.Email)
        {
            var emailTaken = await db.AppUsers
                .AnyAsync(u => u.Email == req.Email && u.TenantId == tenantId && u.Id != id, ct);
            if (emailTaken)
                return Conflict(new { error = "Ya existe un usuario con ese email." });
        }

        if (!Enum.TryParse<UserRole>(req.Role, out var role))
            return BadRequest(new { error = "Rol invalido." });

        user.FullName = req.FullName;
        user.Email = req.Email;
        user.Role = role;
        user.IsActive = req.IsActive;
        user.CanEditPhone = req.CanEditPhone;
        user.AllowedAgentIds = req.AllowedAgentIds ?? [];
        user.Permissions = req.Permissions ?? [];
        db.Entry(user).Property(u => u.Permissions).IsModified = true;

        if (!string.IsNullOrWhiteSpace(req.Password))
            user.PasswordHash = AuthController.HashPassword(req.Password);

        await db.SaveChangesAsync(ct);

        return Ok(new
        {
            user.Id,
            user.TenantId,
            user.FullName,
            user.Email,
            Role = user.Role.ToString(),
            user.IsActive,
            user.CreatedAt,
            user.LastLoginAt,
        });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var tenantId = tenantCtx.TenantId;

        var user = await db.AppUsers
            .FirstOrDefaultAsync(u => u.Id == id && u.TenantId == tenantId, ct);

        if (user is null)
            return NotFound(new { error = "Usuario no encontrado." });

        db.AppUsers.Remove(user);
        await db.SaveChangesAsync(ct);

        return NoContent();
    }
}

using System.IdentityModel.Tokens.Jwt;
using AgentFlow.Infrastructure.Persistence;
using AgentFlow.Infrastructure.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgentFlow.API.Controllers;

public record UpdateProfileRequest(string FullName);
public record ChangeMyPasswordRequest(string CurrentPassword, string NewPassword);

[ApiController]
[Route("api/profile")]
public class ProfileController(AgentFlowDbContext db, IBlobStorageService blobStorage) : ControllerBase
{
    [HttpGet]
    [Microsoft.AspNetCore.Authorization.Authorize]
    public async Task<IActionResult> GetProfile(CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        var user = await db.AppUsers.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return NotFound();

        return Ok(new
        {
            user.Id, user.FullName, user.Email,
            Role = user.Role.ToString(),
            user.AvatarUrl, user.CanEditPhone, user.CreatedAt
        });
    }

    [HttpPut]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest req, CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        var user = await db.AppUsers.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return NotFound();

        user.FullName = req.FullName;
        await db.SaveChangesAsync(ct);

        return Ok(new { user.Id, user.FullName, user.Email, Role = user.Role.ToString(), user.AvatarUrl });
    }

    [HttpPost("avatar")]
    public async Task<IActionResult> UploadAvatar(IFormFile photo, CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        var user = await db.AppUsers.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return NotFound();

        if (photo.Length > 5 * 1024 * 1024)
            return BadRequest(new { error = "La foto no puede superar 5 MB." });

        var allowedTypes = new[] { "image/jpeg", "image/png", "image/webp" };
        if (!allowedTypes.Contains(photo.ContentType.ToLower()))
            return BadRequest(new { error = "Formato no soportado. Use JPG, PNG o WebP." });

        var ext = photo.ContentType.ToLower() switch
        {
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => ".jpg"
        };

        var blobPath = $"profiles/{user.TenantId}/{user.Id}{ext}";

        // Eliminar avatar anterior si existe
        if (!string.IsNullOrEmpty(user.AvatarUrl))
        {
            try { await blobStorage.DeleteAsync(ExtractBlobPath(user.AvatarUrl), ct); } catch { }
        }

        using var stream = photo.OpenReadStream();
        var url = await blobStorage.UploadAsync(blobPath, stream, photo.ContentType, ct);

        user.AvatarUrl = url;
        await db.SaveChangesAsync(ct);

        return Ok(new { avatarUrl = url });
    }

    [HttpDelete("avatar")]
    public async Task<IActionResult> DeleteAvatar(CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        var user = await db.AppUsers.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return NotFound();

        if (!string.IsNullOrEmpty(user.AvatarUrl))
        {
            try { await blobStorage.DeleteAsync(ExtractBlobPath(user.AvatarUrl), ct); } catch { }
            user.AvatarUrl = null;
            await db.SaveChangesAsync(ct);
        }

        return Ok(new { message = "Avatar eliminado." });
    }

    [HttpPost("change-password")]
    [Microsoft.AspNetCore.Authorization.Authorize]
    public async Task<IActionResult> ChangeMyPassword([FromBody] ChangeMyPasswordRequest req, CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        var user = await db.AppUsers.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return NotFound();

        if (!AuthController.VerifyPassword(req.CurrentPassword, user.PasswordHash))
            return BadRequest(new { error = "La contrasena actual es incorrecta." });

        if (req.NewPassword.Length < 8)
            return BadRequest(new { error = "La nueva contrasena debe tener al menos 8 caracteres." });

        user.PasswordHash = AuthController.HashPassword(req.NewPassword);
        await db.SaveChangesAsync(ct);

        return Ok(new { message = "Contrasena actualizada correctamente." });
    }

    private Guid? GetUserId()
    {
        var sub = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
               ?? User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
               ?? User.FindFirst("sub")?.Value;
        return sub is not null && Guid.TryParse(sub, out var id) ? id : null;
    }

    private static string ExtractBlobPath(string url)
    {
        var uri = new Uri(url);
        var path = uri.AbsolutePath;
        var containerStart = path.IndexOf('/', 1);
        return containerStart >= 0 ? path[(containerStart + 1)..] : path.TrimStart('/');
    }
}

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

        // Límite de 3 MB para avatares (base64 lo aumenta un 33%)
        if (photo.Length > 3 * 1024 * 1024)
            return BadRequest(new { error = "La foto no puede superar 3 MB." });

        // Detectar tipo real por los primeros bytes (magic bytes)
        string detectedMime;
        byte[] fileBytes;
        using (var stream = photo.OpenReadStream())
        {
            using var ms = new System.IO.MemoryStream();
            await stream.CopyToAsync(ms, ct);
            fileBytes = ms.ToArray();
        }

        if (fileBytes.Length < 4)
            return BadRequest(new { error = "Archivo inválido." });

        detectedMime = DetectImageMime(fileBytes[..4], photo.ContentType);
        if (detectedMime == "unknown")
            return BadRequest(new { error = "Formato no soportado. Use JPG, PNG, WebP o GIF." });

        // Convertir a data URL base64 — se almacena directamente en la BD
        // sin depender de Azure Blob Storage ni de ningún servicio externo
        var base64 = Convert.ToBase64String(fileBytes);
        var dataUrl = $"data:{detectedMime};base64,{base64}";

        user.AvatarUrl = dataUrl;
        await db.SaveChangesAsync(ct);

        return Ok(new { avatarUrl = dataUrl });
    }

    /// <summary>
    /// Sirve el avatar de un usuario.
    /// - Si AvatarUrl es una data URL (base64), la decodifica y la devuelve como imagen.
    /// - Si AvatarUrl es una ruta blob (legacy), la descarga de Azure y la retransmite.
    /// </summary>
    [HttpGet("avatar-img/{userId:guid}")]
    [Microsoft.AspNetCore.Authorization.AllowAnonymous]
    public async Task<IActionResult> GetAvatarImage(Guid userId, CancellationToken ct)
    {
        var user = await db.AppUsers
            .Where(u => u.Id == userId)
            .Select(u => new { u.AvatarUrl })
            .FirstOrDefaultAsync(ct);

        if (user?.AvatarUrl is null) return NotFound();

        // Nuevo formato: data URL base64 almacenada directamente en BD
        if (user.AvatarUrl.StartsWith("data:"))
        {
            try
            {
                // Formato: "data:{mime};base64,{base64data}"
                var comma = user.AvatarUrl.IndexOf(',');
                if (comma < 0) return BadRequest();
                var meta = user.AvatarUrl[5..comma]; // quitar "data:"
                var mime = meta.Contains(';') ? meta[..meta.IndexOf(';')] : meta;
                var base64 = user.AvatarUrl[(comma + 1)..];
                var bytes = Convert.FromBase64String(base64);
                return File(bytes, mime);
            }
            catch
            {
                return BadRequest();
            }
        }

        // Legacy: ruta blob (Azure) o URL absoluta
        var blobPath = user.AvatarUrl.StartsWith("http")
            ? ExtractBlobPath(user.AvatarUrl)
            : user.AvatarUrl;

        try
        {
            var (stream, contentType) = await blobStorage.DownloadAsync(blobPath, ct);
            return File(stream, contentType);
        }
        catch
        {
            return NotFound();
        }
    }

    private static string DetectImageMime(byte[] header, string fallbackMime)
    {
        // PNG: 89 50 4E 47
        if (header.Length >= 4 && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47)
            return "image/png";
        // JPEG: FF D8 FF
        if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
            return "image/jpeg";
        // GIF: 47 49 46 38
        if (header.Length >= 4 && header[0] == 0x47 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x38)
            return "image/gif";
        // WebP: 52 49 46 46 (RIFF)
        if (header.Length >= 4 && header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46)
            return "image/webp";
        // Confiar en el Content-Type del navegador si es imagen conocida
        var allowed = new[] { "image/jpeg", "image/png", "image/webp", "image/gif" };
        return allowed.Contains(fallbackMime.ToLower()) ? fallbackMime : "unknown";
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

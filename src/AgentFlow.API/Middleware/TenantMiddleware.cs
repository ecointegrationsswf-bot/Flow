using AgentFlow.Domain.Interfaces;

namespace AgentFlow.API.Middleware;

/// <summary>
/// Resuelve el tenant desde el JWT claim "tenant_id" o desde el header X-Tenant-Slug.
/// Webhooks de UltraMsg/Meta llegan con un token que mapea al tenant en la BD.
/// </summary>
public class TenantMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        var tenantId = ctx.User.FindFirst("tenant_id")?.Value
                    ?? ctx.Request.Headers["X-Tenant-Id"].FirstOrDefault()
                    ?? ctx.Request.Query["tenantId"].FirstOrDefault();

        if (tenantId is not null && Guid.TryParse(tenantId, out var id))
        {
            ctx.Items["TenantId"] = id;
        }

        await next(ctx);
    }
}

public class HttpTenantContext(IHttpContextAccessor accessor) : ITenantContext
{
    public Guid TenantId
    {
        get
        {
            var val = accessor.HttpContext?.Items["TenantId"];
            return val is Guid id ? id : Guid.Empty;
        }
    }
    public string TenantSlug => accessor.HttpContext?.User.FindFirst("tenant_slug")?.Value ?? "";
}

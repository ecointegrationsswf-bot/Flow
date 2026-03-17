using System.Text.Json;
using AgentFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentFlow.Infrastructure.Persistence.Configurations;

public class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> b)
    {
        b.HasKey(u => u.Id);
        b.HasIndex(u => new { u.TenantId, u.Email }).IsUnique();
        b.Property(u => u.FullName).HasMaxLength(300).IsRequired();
        b.Property(u => u.Email).HasMaxLength(300).IsRequired();
        b.Property(u => u.PasswordHash).HasMaxLength(500).IsRequired();
        b.Property(u => u.Role).HasConversion<string>().HasMaxLength(30);
        b.Property(u => u.AllowedAgentIds)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<Guid>>(v, (JsonSerializerOptions?)null) ?? new List<Guid>()
            ).HasMaxLength(2000);
        b.HasOne(u => u.Tenant).WithMany().HasForeignKey(u => u.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

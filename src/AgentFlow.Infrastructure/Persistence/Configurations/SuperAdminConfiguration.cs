using AgentFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentFlow.Infrastructure.Persistence.Configurations;

public class SuperAdminConfiguration : IEntityTypeConfiguration<SuperAdmin>
{
    public void Configure(EntityTypeBuilder<SuperAdmin> b)
    {
        b.HasKey(s => s.Id);
        b.HasIndex(s => s.Email).IsUnique();
        b.Property(s => s.Email).HasMaxLength(300).IsRequired();
        b.Property(s => s.PasswordHash).HasMaxLength(500).IsRequired();
        b.Property(s => s.FullName).HasMaxLength(300).IsRequired();
    }
}

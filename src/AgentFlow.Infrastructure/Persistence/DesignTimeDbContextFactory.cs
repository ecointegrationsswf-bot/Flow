using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace AgentFlow.Infrastructure.Persistence;

/// <summary>
/// Factory para que dotnet ef pueda crear el DbContext en design-time
/// sin ejecutar Program.cs (evita errores de Redis, Hangfire, etc.)
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AgentFlowDbContext>
{
    public AgentFlowDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Path.Combine(Directory.GetCurrentDirectory(), "..", "AgentFlow.API"))
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .Build();

        var optionsBuilder = new DbContextOptionsBuilder<AgentFlowDbContext>();
        optionsBuilder.UseSqlServer(
            configuration.GetConnectionString("DefaultConnection"),
            sql => sql.MigrationsAssembly("AgentFlow.Infrastructure"));

        return new AgentFlowDbContext(optionsBuilder.Options);
    }
}

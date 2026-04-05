#!/usr/bin/env dotnet-script
// Test: diagnosticar por qué campaign status no cambia a Completed
// Ejecutar: dotnet script TestContactSent.cs

#r "nuget: Microsoft.EntityFrameworkCore.SqlServer, 8.0.0"
#r "nuget: Microsoft.EntityFrameworkCore, 8.0.0"

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

// ── Connection string de producción ──────────────────────────────────────────
var connStr = "Server=tcp:sql1003.site4now.net,1433;Database=db_ab2fbb_flow;User Id=db_ab2fbb_flow_admin;Password=u0hwjTvMfyFMVxn6x4YM;TrustServerCertificate=True;Encrypt=True;Connection Timeout=30;";
var campaignId = Guid.Parse("6a5c7260-db60-41c7-bec5-2d661c4f844b");

var sqlLog = new List<string>();

var optionsBuilder = new DbContextOptionsBuilder<RawDbContext>();
optionsBuilder
    .UseSqlServer(connStr)
    .LogTo(msg => {
        if (msg.Contains("UPDATE") || msg.Contains("SELECT") || msg.Contains("SET"))
        {
            sqlLog.Add(msg);
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"[SQL] {msg.Trim()}");
            Console.ResetColor();
        }
    }, LogLevel.Information)
    .EnableSensitiveDataLogging();

Console.WriteLine("=== TEST 1: Leer estado actual de la campaña ===");
using (var db = new RawDbContext(optionsBuilder.Options))
{
    var raw = await db.Database.SqlQueryRaw<CampaignRow>(
        "SELECT Id, Status, ProcessedContacts, TotalContacts, CompletedAt FROM Campaigns WHERE Id = {0}", campaignId
    ).FirstOrDefaultAsync();

    if (raw == null) { Console.WriteLine("❌ Campaña no encontrada"); return; }
    Console.WriteLine($"  Status          = '{raw.Status}'");
    Console.WriteLine($"  ProcessedContacts = {raw.ProcessedContacts}");
    Console.WriteLine($"  TotalContacts   = {raw.TotalContacts}");
    Console.WriteLine($"  CompletedAt     = {raw.CompletedAt?.ToString() ?? "NULL"}");
}

Console.WriteLine();
Console.WriteLine("=== TEST 2: Simular ExecuteUpdateAsync (método viejo) ===");
Console.WriteLine("  Generando SQL con enum entero vs string...");
using (var db = new RawDbContext(optionsBuilder.Options))
{
    // Simular el WHERE que generaba el código anterior
    var count = await db.Database.ExecuteSqlRawAsync(
        "UPDATE Campaigns SET Status = 'TestViejo' WHERE Id = {0} AND Status != {1} AND Status != {2} AND 1=0",
        campaignId, "Completed", "Failed");
    Console.WriteLine($"  Filas afectadas (simulado viejo con string): {count}");
}

Console.WriteLine();
Console.WriteLine("=== TEST 3: Simular SaveChangesAsync (método nuevo) ===");
using (var db = new RawDbContext(optionsBuilder.Options))
{
    var campaign = await db.Campaigns.FirstOrDefaultAsync(c => c.Id == campaignId);
    if (campaign != null)
    {
        Console.WriteLine($"  Estado cargado por EF: '{campaign.Status}'");
        Console.WriteLine($"  ProcessedContacts: {campaign.ProcessedContacts}, TotalContacts: {campaign.TotalContacts}");

        // Simular la condición
        bool conditionMet = campaign.ProcessedContacts >= campaign.TotalContacts
                         && campaign.TotalContacts > 0
                         && campaign.Status != "Completed"
                         && campaign.Status != "Failed";
        Console.WriteLine($"  Condición de auto-complete: {conditionMet}");

        if (conditionMet)
        {
            Console.WriteLine("  → Intentando cambiar Status a 'Completed' via change tracker...");
            campaign.Status = "Completed";
            campaign.CompletedAt = DateTime.UtcNow;
            int rows = await db.SaveChangesAsync();
            Console.WriteLine($"  Filas guardadas: {rows}");
        }
    }
}

Console.WriteLine();
Console.WriteLine("=== TEST 4: Verificar estado final en BD ===");
using (var db = new RawDbContext(optionsBuilder.Options))
{
    var raw = await db.Database.SqlQueryRaw<CampaignRow>(
        "SELECT Id, Status, ProcessedContacts, TotalContacts, CompletedAt FROM Campaigns WHERE Id = {0}", campaignId
    ).FirstOrDefaultAsync();

    Console.WriteLine($"  Status FINAL    = '{raw?.Status}'");
    Console.WriteLine($"  CompletedAt     = {raw?.CompletedAt?.ToString() ?? "NULL"}");

    if (raw?.Status == "Completed")
        Console.WriteLine("  ✅ ÉXITO: Status cambió a Completed correctamente");
    else
        Console.WriteLine("  ❌ FALLO: Status NO cambió");
}

Console.WriteLine();
Console.WriteLine("=== TEST 5: Restaurar status a Running para no dañar la campaña ===");
using (var db = new RawDbContext(optionsBuilder.Options))
{
    await db.Database.ExecuteSqlRawAsync(
        "UPDATE Campaigns SET Status = 'Running', CompletedAt = NULL WHERE Id = {0}", campaignId);
    Console.WriteLine("  Campaña restaurada a Running.");
}

Console.WriteLine();
Console.WriteLine("=== SQL LOG COMPLETO ===");
foreach (var s in sqlLog) Console.WriteLine(s);

// ── Mini DbContext solo para el test ─────────────────────────────────────────
public class RawDbContext : DbContext
{
    public RawDbContext(DbContextOptions<RawDbContext> options) : base(options) { }
    public DbSet<CampaignSimple> Campaigns => Set<CampaignSimple>();
}

public class CampaignSimple
{
    public Guid Id { get; set; }
    public string Status { get; set; } = "";
    public int ProcessedContacts { get; set; }
    public int TotalContacts { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public class CampaignRow
{
    public Guid Id { get; set; }
    public string Status { get; set; } = "";
    public int ProcessedContacts { get; set; }
    public int TotalContacts { get; set; }
    public DateTime? CompletedAt { get; set; }
}

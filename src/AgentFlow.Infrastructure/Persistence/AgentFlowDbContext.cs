using AgentFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AgentFlow.Infrastructure.Persistence;

public class AgentFlowDbContext(DbContextOptions<AgentFlowDbContext> options) : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<AgentDefinition> AgentDefinitions => Set<AgentDefinition>();
    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<CampaignContact> CampaignContacts => Set<CampaignContact>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<GestionEvent> GestionEvents => Set<GestionEvent>();
    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<AgentDocument> AgentDocuments => Set<AgentDocument>();
    public DbSet<ConversationLabel> ConversationLabels => Set<ConversationLabel>();
    public DbSet<WhatsAppLine> WhatsAppLines => Set<WhatsAppLine>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.ApplyConfigurationsFromAssembly(typeof(AgentFlowDbContext).Assembly);
        base.OnModelCreating(b);
    }
}

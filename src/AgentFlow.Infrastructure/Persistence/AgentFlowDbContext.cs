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
    public DbSet<CampaignTemplateDocument> CampaignTemplateDocuments => Set<CampaignTemplateDocument>();
    public DbSet<CampaignTemplateDocumentChunk> CampaignTemplateDocumentChunks => Set<CampaignTemplateDocumentChunk>();
    public DbSet<SystemAuditLog> SystemAuditLogs => Set<SystemAuditLog>();
    public DbSet<ConversationLabel> ConversationLabels => Set<ConversationLabel>();
    public DbSet<WhatsAppLine> WhatsAppLines => Set<WhatsAppLine>();
    public DbSet<SuperAdmin> SuperAdmins => Set<SuperAdmin>();
    public DbSet<AgentTemplate> AgentTemplates => Set<AgentTemplate>();
    public DbSet<AgentCategory> AgentCategories => Set<AgentCategory>();
    public DbSet<CampaignTemplate> CampaignTemplates => Set<CampaignTemplate>();
    public DbSet<ActionDefinition> ActionDefinitions => Set<ActionDefinition>();
    public DbSet<TenantActionContract> TenantActionContracts => Set<TenantActionContract>();
    public DbSet<PromptTemplate> PromptTemplates => Set<PromptTemplate>();
    public DbSet<WebhookLog> WebhookLogs => Set<WebhookLog>();
    public DbSet<CampaignDispatchLog> CampaignDispatchLogs => Set<CampaignDispatchLog>();
    public DbSet<AgentRegistryEntry> AgentRegistryEntries => Set<AgentRegistryEntry>();
    public DbSet<ScheduledWebhookJob> ScheduledWebhookJobs => Set<ScheduledWebhookJob>();
    public DbSet<ScheduledWebhookJobExecution> ScheduledWebhookJobExecutions => Set<ScheduledWebhookJobExecution>();
    public DbSet<ScheduledWebhookJobExecutionItem> ScheduledWebhookJobExecutionItems => Set<ScheduledWebhookJobExecutionItem>();
    public DbSet<WebhookDispatchLog> WebhookDispatchLogs => Set<WebhookDispatchLog>();
    public DbSet<InboundMessageQueueItem> InboundMessageQueueItems => Set<InboundMessageQueueItem>();
    public DbSet<InvalidWhatsAppNumber> InvalidWhatsAppNumbers => Set<InvalidWhatsAppNumber>();

    // ── Módulo Morosidad ───────────────────────────────────────────────────────
    public DbSet<LogicalFieldCatalog> LogicalFieldCatalog => Set<LogicalFieldCatalog>();
    public DbSet<ActionFieldMapping> ActionFieldMappings => Set<ActionFieldMapping>();
    public DbSet<ActionDelinquencyConfig> ActionDelinquencyConfigs => Set<ActionDelinquencyConfig>();
    public DbSet<DelinquencyExecution> DelinquencyExecutions => Set<DelinquencyExecution>();
    public DbSet<DelinquencyItem> DelinquencyItems => Set<DelinquencyItem>();
    public DbSet<ContactGroup> ContactGroups => Set<ContactGroup>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.ApplyConfigurationsFromAssembly(typeof(AgentFlowDbContext).Assembly);
        base.OnModelCreating(b);
    }
}

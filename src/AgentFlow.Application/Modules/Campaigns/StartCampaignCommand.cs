using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Enums;
using MediatR;

namespace AgentFlow.Application.Modules.Campaigns;

public record StartCampaignCommand(
    Guid TenantId,
    string Name,
    Guid AgentDefinitionId,
    ChannelType Channel,
    CampaignTrigger Trigger,
    List<ContactRow> Contacts,
    string CreatedByUserId,
    DateTime? ScheduledAt = null
) : IRequest<Guid>;

public record ContactRow(
    string PhoneNumber,
    string? ClientName,
    string? Email,
    string? PolicyNumber,
    string? InsuranceCompany,
    decimal? PendingAmount,
    Dictionary<string, string>? Extra
);

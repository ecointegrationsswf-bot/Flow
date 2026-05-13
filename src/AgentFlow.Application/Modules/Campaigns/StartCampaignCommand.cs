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
    DateTime? ScheduledAt = null,
    Guid? CampaignTemplateId = null
) : IRequest<Guid>;

public record ContactRow(
    string PhoneNumber,
    string? ClientName,
    string? Email,
    string? PolicyNumber,
    string? InsuranceCompany,
    decimal? PendingAmount,
    Dictionary<string, string>? Extra,
    // Cuando viene del FixedFormatCampaignService el teléfono ya está en E.164
    // y los datos consolidados se entregan aquí como JSON.
    string? ContactDataJson = null,
    bool IsAlreadyE164 = false
);

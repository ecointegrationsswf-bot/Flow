SELECT
  ct.Name AS TemplateName,
  ct.Id AS TemplateId,
  LEN(ct.ActionConfigs) AS ConfigLen,
  CASE
    WHEN ct.ActionConfigs IS NULL THEN 'NULL'
    WHEN LEFT(ct.ActionConfigs, 6) = '{"0":"' THEN 'CORRUPTO'
    WHEN ct.ActionConfigs LIKE '%triggerConfig%' THEN 'CON_TRIGGER'
    ELSE 'SIN_TRIGGER'
  END AS Estado
FROM CampaignTemplates ct
WHERE ct.TenantId = '2D00233D-C44A-4345-841C-1CCCC425626A' AND ct.IsActive = 1;

-- Agentes y sus templates vinculados
SELECT r.Slug, r.Name, r.CampaignTemplateId, r.IsWelcome, ct.Name AS TemplateName
FROM AgentRegistryEntries r
LEFT JOIN CampaignTemplates ct ON ct.Id = r.CampaignTemplateId
WHERE r.TenantId = '2D00233D-C44A-4345-841C-1CCCC425626A';

-- Ultima conversacion
SELECT TOP 3 c.Id, c.ClientPhone, c.Status, c.LastActivityAt,
  c.CampaignId, c.ActiveAgentId, a.Name AS AgentName
FROM Conversations c
LEFT JOIN AgentDefinitions a ON a.Id = c.ActiveAgentId
WHERE c.TenantId = '2D00233D-C44A-4345-841C-1CCCC425626A'
ORDER BY c.LastActivityAt DESC;

-- 1. ActionConfigs del template que el usuario quiere usar
SELECT LEN(ActionConfigs) AS Len,
  LEFT(ActionConfigs, 100) AS First100,
  CASE WHEN LEFT(ActionConfigs, 6) = '{"0":"' THEN 'CORRUPTO' ELSE 'OK' END AS Estado
FROM CampaignTemplates
WHERE Id = 'BB7AD0FA-5E45-4ADE-84A1-FB503D51B863';

-- 2. ActionIds del mismo template (las acciones seleccionadas en la UI)
SELECT ActionIds FROM CampaignTemplates WHERE Id = 'BB7AD0FA-5E45-4ADE-84A1-FB503D51B863';

-- 3. Que agente del Cerebro apunta a este template?
SELECT Slug, Name, CampaignTemplateId
FROM AgentRegistryEntries
WHERE TenantId = '2D00233D-C44A-4345-841C-1CCCC425626A'
  AND CampaignTemplateId = 'BB7AD0FA-5E45-4ADE-84A1-FB503D51B863';

-- 4. Que agente procesa la conversacion activa?
SELECT TOP 1 c.Id, c.ClientPhone, c.ActiveAgentId, c.CampaignId, c.Status,
  cam.CampaignTemplateId AS TemplateUsado
FROM Conversations c
LEFT JOIN Campaigns cam ON cam.Id = c.CampaignId
WHERE c.TenantId = '2D00233D-C44A-4345-841C-1CCCC425626A'
  AND c.ClientPhone = '+50768777386'
ORDER BY c.LastActivityAt DESC;

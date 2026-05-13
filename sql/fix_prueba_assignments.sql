-- Re-pobla AssignedPromptIds y AssignedActionIds del tenant Prueba
-- con los IDs realmente usados por sus maestros activos.
DECLARE @tenant UNIQUEIDENTIFIER = '2D00233D-C44A-4345-841C-1CCCC425626A';

DECLARE @prompts NVARCHAR(MAX);
SELECT @prompts = '[' + STRING_AGG('"' + LOWER(CAST([value] AS NVARCHAR(36))) + '"', ',') + ']'
FROM (
    SELECT DISTINCT [value]
    FROM CampaignTemplates ct
    CROSS APPLY OPENJSON(ct.PromptTemplateIds)
    WHERE ct.TenantId = @tenant AND ct.IsActive = 1
) d;

DECLARE @actions NVARCHAR(MAX);
SELECT @actions = '[' + STRING_AGG('"' + LOWER(CAST([value] AS NVARCHAR(36))) + '"', ',') + ']'
FROM (
    SELECT DISTINCT [value]
    FROM CampaignTemplates ct
    CROSS APPLY OPENJSON(ct.ActionIds)
    WHERE ct.TenantId = @tenant AND ct.IsActive = 1
) d;

UPDATE Tenants
SET AssignedPromptIds = COALESCE(@prompts, ''),
    AssignedActionIds = COALESCE(@actions, '')
WHERE Id = @tenant;

SELECT Name, AssignedPromptIds, AssignedActionIds
FROM Tenants
WHERE Id = @tenant;

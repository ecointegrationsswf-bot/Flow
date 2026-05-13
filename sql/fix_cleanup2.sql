SET QUOTED_IDENTIFIER ON;
GO

DECLARE @clean NVARCHAR(MAX);
DECLARE @src NVARCHAR(MAX);

SELECT @src = ActionConfigs FROM CampaignTemplates WHERE Id = 'BB7AD0FA-5E45-4ADE-84A1-FB503D51B863';

-- Extraer solo las entradas con keys que son GUIDs validos
SELECT @clean = '{' +
  STUFF((
    SELECT ',' + '"' + p.[key] + '":' + p.[value]
    FROM OPENJSON(@src) p
    WHERE TRY_CAST(p.[key] AS UNIQUEIDENTIFIER) IS NOT NULL
    FOR XML PATH(''), TYPE
  ).value('.','nvarchar(max)'), 1, 1, '') + '}';

UPDATE CampaignTemplates
SET ActionConfigs = @clean
WHERE Id = 'BB7AD0FA-5E45-4ADE-84A1-FB503D51B863';

SELECT LEN(@clean) AS NewLen, LEFT(@clean, 100) AS First100;
GO

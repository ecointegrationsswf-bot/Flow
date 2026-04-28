-- Limpiar la parte corrupta (indices 0-87) del ActionConfigs de BB7AD0FA
-- Reconstruir solo con las entradas validas (GUIDs reales)
UPDATE CampaignTemplates
SET ActionConfigs = (
  SELECT '{' +
    STUFF((
      SELECT ',' + '"' + p.[key] + '":' + p.[value]
      FROM OPENJSON(ActionConfigs) p
      WHERE TRY_CAST(p.[key] AS UNIQUEIDENTIFIER) IS NOT NULL
      FOR XML PATH(''), TYPE
    ).value('.','nvarchar(max)'), 1, 1, '') + '}'
  FROM CampaignTemplates sub
  WHERE sub.Id = 'BB7AD0FA-5E45-4ADE-84A1-FB503D51B863'
)
WHERE Id = 'BB7AD0FA-5E45-4ADE-84A1-FB503D51B863';

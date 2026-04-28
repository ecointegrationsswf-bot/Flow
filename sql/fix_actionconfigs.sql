-- Reparar ActionConfigs doble-serializados en produccion
-- El JSON corrupto tiene formato {"0":"{","1":"\"" ...} que es un string serializado char-by-char
-- Necesitamos reconstruir el string original concatenando los valores en orden

-- Primero verificamos cuales estan corruptos
SELECT Id, Name, LEN(ActionConfigs) AS Len
FROM CampaignTemplates
WHERE TenantId = '2D00233D-C44A-4345-841C-1CCCC425626A'
  AND ActionConfigs IS NOT NULL
  AND LEFT(ActionConfigs, 6) = '{"0":"';

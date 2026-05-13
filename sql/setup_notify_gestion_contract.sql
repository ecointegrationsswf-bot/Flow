-- ──────────────────────────────────────────────────────────────────
-- DefaultWebhookContract de NOTIFY_GESTION (corregido)
--
-- Mapping (alineado al Excel del tenant Prueba):
--   keyValue          ← contact.KeyValue (columna del Excel)
--   Json.Comentario   ← result.comentario (labeling)
--   Json.FechaPago    ← result.fechaPago  (labeling)
--   Json.MontoPagar   ← result.montoPagar (labeling)
--   Json.FechaGestion ← conversation.label.labeledAt (sistema)
-- ──────────────────────────────────────────────────────────────────
DECLARE @contract NVARCHAR(MAX) = N'{
  "webhookUrl": "https://www.somossegurosapp.com:693/sf.yoseguroapi/api/polizas/set_generar_gestiones",
  "webhookMethod": "POST",
  "contentType": "application/json",
  "structure": "nested",
  "authType": "None",
  "timeoutSeconds": 30,
  "inputSchema": {
    "contentType": "application/json",
    "httpMethod": "POST",
    "structure": "nested",
    "fields": [
      {"fieldPath":"keyValue","sourceType":"system","sourceKey":"contact.KeyValue","dataType":"string","required":true},
      {"fieldPath":"Json.Comentario","sourceType":"labelingResult","sourceKey":"comentario","dataType":"string","required":false},
      {"fieldPath":"Json.FechaPago","sourceType":"labelingResult","sourceKey":"fechaPago","dataType":"string","required":false},
      {"fieldPath":"Json.MontoPagar","sourceType":"labelingResult","sourceKey":"montoPagar","dataType":"number","required":false},
      {"fieldPath":"Json.FechaGestion","sourceType":"system","sourceKey":"conversation.label.labeledAt","dataType":"date","required":false}
    ]
  },
  "outputSchema": {"fields": []}
}';

UPDATE ActionDefinitions
SET DefaultWebhookContract = @contract,
    UpdatedAt = GETUTCDATE()
WHERE Name = 'NOTIFY_GESTION' AND TenantId IS NULL;

SELECT Name, LEN(DefaultWebhookContract) AS contractLen FROM ActionDefinitions
WHERE Name = 'NOTIFY_GESTION' AND TenantId IS NULL;

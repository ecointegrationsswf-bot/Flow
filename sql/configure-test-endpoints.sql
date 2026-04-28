-- ============================================================================
-- Configuración Webhook Contract System para tenant "Prueba"
--   Tenant  : 2D00233D-C44A-4345-841C-1CCCC425626A
--   Template: Cobros Abril 2026  (E3D24A54-D3B2-4449-9256-4760A18373C2)
--
-- Crea dos ActionDefinitions apuntando a los endpoints de test publicados en
-- producción:
--   - GENERATE_TEST_PDF  → /api/webhook-test-endpoints/generate-pdf
--   - SEND_TEST_EMAIL    → /api/webhook-test-endpoints/send-summary-email
--
-- Las agrega al ActionIds del template y sustituye ActionConfigs con los
-- contratos (InputSchema + OutputSchema) de cada una. También actualiza el
-- SystemPrompt del template para que el agente emita los tags
-- [ACTION:GENERATE_TEST_PDF] y [ACTION:SEND_TEST_EMAIL] cuando el cliente pida
-- un comprobante o un correo con resumen.
-- ============================================================================

SET NOCOUNT ON;

DECLARE @TenantId UNIQUEIDENTIFIER  = '2D00233D-C44A-4345-841C-1CCCC425626A';
DECLARE @TemplateId UNIQUEIDENTIFIER = 'E3D24A54-D3B2-4449-9256-4760A18373C2';

-- GUIDs fijos para los dos nuevos ActionDefinitions (idempotente)
DECLARE @PdfActionId   UNIQUEIDENTIFIER = 'AA000001-1111-4000-8000-000000000001';
DECLARE @EmailActionId UNIQUEIDENTIFIER = 'AA000002-2222-4000-8000-000000000002';

-- URL base del backend en producción
DECLARE @BaseUrl NVARCHAR(500) = 'http://jamconsulting-004-site12.site4future.com';

-- ── 1) Habilitar feature flag por tenant ──
UPDATE Tenants
SET    WebhookContractEnabled = 1
WHERE  Id = @TenantId;

PRINT 'Tenant Prueba: WebhookContractEnabled = 1';

-- ── 2) Upsert ActionDefinition: GENERATE_TEST_PDF ──
IF NOT EXISTS (SELECT 1 FROM ActionDefinitions WHERE Id = @PdfActionId)
BEGIN
    INSERT INTO ActionDefinitions (
        Id, TenantId, Name, Description,
        RequiresWebhook, SendsEmail, SendsSms,
        WebhookUrl, WebhookMethod,
        ExecutionMode, ParamSource, ConversationImpact,
        RequiredParams, IsActive, CreatedAt
    )
    VALUES (
        @PdfActionId, @TenantId, 'GENERATE_TEST_PDF',
        'Genera un PDF de prueba con el teléfono del contacto embedido.',
        1, 0, 0,
        @BaseUrl + '/api/webhook-test-endpoints/generate-pdf', 'POST',
        'Inline', 'SystemOnly', 'BlocksResponse',
        NULL, 1, SYSUTCDATETIME()
    );
    PRINT 'ActionDefinition creado: GENERATE_TEST_PDF';
END
ELSE
BEGIN
    UPDATE ActionDefinitions
    SET Name = 'GENERATE_TEST_PDF',
        Description = 'Genera un PDF de prueba con el teléfono del contacto embedido.',
        RequiresWebhook = 1,
        WebhookUrl = @BaseUrl + '/api/webhook-test-endpoints/generate-pdf',
        WebhookMethod = 'POST',
        ExecutionMode = 'Inline',
        ParamSource = 'SystemOnly',
        ConversationImpact = 'BlocksResponse',
        IsActive = 1,
        UpdatedAt = SYSUTCDATETIME()
    WHERE Id = @PdfActionId;
    PRINT 'ActionDefinition actualizado: GENERATE_TEST_PDF';
END

-- ── 3) Upsert ActionDefinition: SEND_TEST_EMAIL ──
IF NOT EXISTS (SELECT 1 FROM ActionDefinitions WHERE Id = @EmailActionId)
BEGIN
    INSERT INTO ActionDefinitions (
        Id, TenantId, Name, Description,
        RequiresWebhook, SendsEmail, SendsSms,
        WebhookUrl, WebhookMethod,
        ExecutionMode, ParamSource, ConversationImpact,
        RequiredParams, IsActive, CreatedAt
    )
    VALUES (
        @EmailActionId, @TenantId, 'SEND_TEST_EMAIL',
        'Envía un correo de resumen a una dirección configurada.',
        1, 1, 0,
        @BaseUrl + '/api/webhook-test-endpoints/send-summary-email', 'POST',
        'Inline', 'SystemOnly', 'BlocksResponse',
        NULL, 1, SYSUTCDATETIME()
    );
    PRINT 'ActionDefinition creado: SEND_TEST_EMAIL';
END
ELSE
BEGIN
    UPDATE ActionDefinitions
    SET Name = 'SEND_TEST_EMAIL',
        Description = 'Envía un correo de resumen a una dirección configurada.',
        RequiresWebhook = 1,
        SendsEmail = 1,
        WebhookUrl = @BaseUrl + '/api/webhook-test-endpoints/send-summary-email',
        WebhookMethod = 'POST',
        ExecutionMode = 'Inline',
        ParamSource = 'SystemOnly',
        ConversationImpact = 'BlocksResponse',
        IsActive = 1,
        UpdatedAt = SYSUTCDATETIME()
    WHERE Id = @EmailActionId;
    PRINT 'ActionDefinition actualizado: SEND_TEST_EMAIL';
END

-- ── 4) ActionConfigs JSON con InputSchema + OutputSchema ──
-- PDF: usa contact.phone del sistema como único campo obligatorio.
-- Email: usa toEmail y summary estáticos (Phase 4 no soporta ConversationOnly).
DECLARE @ActionConfigs NVARCHAR(MAX) =
N'{
  "aa000001-1111-4000-8000-000000000001": {
    "webhookUrl": "' + @BaseUrl + N'/api/webhook-test-endpoints/generate-pdf",
    "webhookMethod": "POST",
    "contentType": "application/json",
    "structure": "flat",
    "authType": "None",
    "timeoutSeconds": 15,
    "inputSchema": {
      "contentType": "application/json",
      "httpMethod": "POST",
      "structure": "flat",
      "fields": [
        { "fieldPath": "phone",      "sourceType": "system", "sourceKey": "contact.phone",         "dataType": "string", "required": true  },
        { "fieldPath": "clientName", "sourceType": "system", "sourceKey": "contact.name",          "dataType": "string", "required": false },
        { "fieldPath": "title",      "sourceType": "static", "staticValue": "Comprobante de prueba",           "dataType": "string", "required": false },
        { "fieldPath": "notes",      "sourceType": "static", "staticValue": "Documento generado por AgentFlow para probar el flujo de webhooks.", "dataType": "string", "required": false }
      ]
    },
    "outputSchema": {
      "fields": [
        { "fieldPath": "message",    "dataType": "string", "outputAction": "send_to_agent",       "label": "Estado",  "required": true  },
        { "fieldPath": "fileName",   "dataType": "string", "outputAction": "send_to_agent",       "label": "Archivo", "required": false },
        { "fieldPath": "sizeBytes",  "dataType": "number", "outputAction": "send_to_agent",       "label": "Tamano",  "required": false },
        { "fieldPath": "fileBase64", "dataType": "base64", "mimeType": "application/pdf", "outputAction": "send_whatsapp_media", "label": "PDF", "required": true }
      ]
    }
  },
  "aa000002-2222-4000-8000-000000000002": {
    "webhookUrl": "' + @BaseUrl + N'/api/webhook-test-endpoints/send-summary-email",
    "webhookMethod": "POST",
    "contentType": "application/json",
    "structure": "flat",
    "authType": "None",
    "timeoutSeconds": 15,
    "inputSchema": {
      "contentType": "application/json",
      "httpMethod": "POST",
      "structure": "flat",
      "fields": [
        { "fieldPath": "toEmail",    "sourceType": "static", "staticValue": "vanessalucumi28121@gmail.com", "dataType": "string", "required": true  },
        { "fieldPath": "subject",    "sourceType": "static", "staticValue": "Resumen solicitado (prueba)",   "dataType": "string", "required": false },
        { "fieldPath": "summary",    "sourceType": "static", "staticValue": "El cliente solicitó un resumen de la conversación. Este es un correo de prueba del Webhook Contract System.", "dataType": "string", "required": true },
        { "fieldPath": "clientName", "sourceType": "system", "sourceKey": "contact.name",  "dataType": "string", "required": false },
        { "fieldPath": "phone",      "sourceType": "system", "sourceKey": "contact.phone", "dataType": "string", "required": false }
      ]
    },
    "outputSchema": {
      "fields": [
        { "fieldPath": "message", "dataType": "string", "outputAction": "send_to_agent", "label": "Estado",  "required": true },
        { "fieldPath": "sentTo",  "dataType": "string", "outputAction": "send_to_agent", "label": "Correo", "required": false }
      ]
    }
  }
}';

-- ── 5) Actualizar CampaignTemplate: ActionIds + ActionConfigs + SystemPrompt ──
DECLARE @NewActionIds NVARCHAR(MAX) =
N'["d221c23d-fa0c-41ad-b356-60df013c877f","1df8854f-4932-45f9-bf74-2b81377f8969","aa000001-1111-4000-8000-000000000001","aa000002-2222-4000-8000-000000000002"]';

DECLARE @NewSystemPrompt NVARCHAR(MAX) = N'Eres un agente de cobros de Somos Seguros para pruebas del sistema.
Habla de manera amable y profesional con el cliente en español panameño.

ACCIONES DISPONIBLES (Webhook Contract System — solo para pruebas):
- Si el cliente pide un comprobante, factura, documento o PDF, responde con una frase corta y añade el tag exacto al final: [ACTION:GENERATE_TEST_PDF]
- Si el cliente pide que le envíes un resumen, el detalle o un correo, responde con una frase corta y añade el tag exacto al final: [ACTION:SEND_TEST_EMAIL]
- Usa un solo tag por mensaje. Nunca expliques al cliente que se ejecutó una acción; el sistema lo hará.
- Si el cliente solo quiere conversar, no emitas ningún tag.

Contexto del contacto:
- Nombre del cliente: {{contact.name}}
- Teléfono: {{contact.phone}}

Cierra cada respuesta de forma natural.';

UPDATE CampaignTemplates
SET    ActionIds     = @NewActionIds,
       ActionConfigs = @ActionConfigs,
       SystemPrompt  = @NewSystemPrompt,
       UpdatedAt     = SYSUTCDATETIME()
WHERE  Id = @TemplateId;

PRINT 'CampaignTemplate "Cobros Abril 2026" actualizado: ActionIds + ActionConfigs + SystemPrompt';

-- ── 6) Verificación ──
SELECT Name, WebhookContractEnabled FROM Tenants WHERE Id = @TenantId;

SELECT Name, RequiresWebhook, ExecutionMode, ParamSource, ConversationImpact, WebhookUrl
FROM   ActionDefinitions
WHERE  TenantId = @TenantId AND Name IN ('GENERATE_TEST_PDF', 'SEND_TEST_EMAIL');

SELECT Name, LEN(CAST(ActionConfigs AS NVARCHAR(MAX))) AS CfgLen, LEN(SystemPrompt) AS PromptLen
FROM   CampaignTemplates
WHERE  Id = @TemplateId;

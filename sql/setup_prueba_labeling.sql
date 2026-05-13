-- ──────────────────────────────────────────────────────────────────
-- Configurar prompts de etiquetado para el tenant Prueba
-- ──────────────────────────────────────────────────────────────────
DECLARE @analysisPrompt NVARCHAR(MAX) = N'Actúa como un sistema de análisis conversacional especializado en gestión de cobros de seguros.
Lee el historial completo de la conversación entre el asistente virtual y el cliente.

REGLAS:
1. Analiza el historial completo, prestando atención a la última intención del cliente.
2. Elige EXACTAMENTE UNA etiqueta de la lista proporcionada (campo "labelName") — debe coincidir EXACTAMENTE con el Nombre.
3. El campo "confidence" debe ser un decimal entre 0.0 y 1.0 (1.0 = certeza total).
4. El campo "reasoning" debe ser una sola frase clara, en español, sin citar mensajes literales.
5. Si la conversación menciona una fecha futura (compromiso de pago, cita, vencimiento) inclúyela en "extractedDate" en formato YYYY-MM-DD. Si no hay fecha, usa null.
6. Además, debes producir un objeto "result" con la estructura exacta del schema proporcionado, extrayendo la información correspondiente del diálogo.
7. Responde ÚNICAMENTE con JSON válido, sin markdown ni texto adicional.';

DECLARE @resultSchema NVARCHAR(MAX) = N'{
  "comentario": "Resumen claro y breve de la conversación en una sola frase, en español. Describe brevemente lo que sucedió y la última intención o compromiso del cliente.",
  "fechaPago": "Si el cliente comprometió una fecha de pago futura, devuelve la fecha en formato yyyy-MM-dd. Si no hay compromiso, usa null.",
  "montoPagar": "Si el cliente acordó un monto específico a pagar, devuelve el número (ej: 150.00). Si no hay monto acordado, usa 0."
}';

UPDATE Tenants
SET LabelingAnalysisPrompt = @analysisPrompt,
    LabelingResultSchemaPrompt = @resultSchema
WHERE Name = 'Prueba';

SELECT Name, LEN(LabelingAnalysisPrompt) AS analysisLen, LEN(LabelingResultSchemaPrompt) AS schemaLen
FROM Tenants WHERE Name = 'Prueba';

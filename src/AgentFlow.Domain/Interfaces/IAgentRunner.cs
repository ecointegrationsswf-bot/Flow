using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Webhooks;

namespace AgentFlow.Domain.Interfaces;

/// <summary>
/// Ejecuta un agente IA dado el contexto de la conversación.
/// Retorna la respuesta generada y metadata de la clasificación.
/// </summary>
public interface IAgentRunner
{
    Task<AgentResponse> RunAsync(AgentRunRequest request, CancellationToken ct = default);
}

public record AgentRunRequest(
    AgentDefinition Agent,
    Conversation Conversation,
    string IncomingMessage,
    List<Message> RecentHistory,
    Dictionary<string, string>? ClientContext = null,   // saldo, póliza, etc.
    string? TenantLlmApiKey = null,                     // API key del tenant para el LLM
    string? MediaUrl = null,                            // URL de imagen para visión IA
    string? MediaType = null,                           // "image" | "document" | "audio"
    List<int>? AttentionDays = null,                    // días laborables (0=Dom…6=Sáb)
    string? AttentionStartTime = null,                  // "HH:mm"
    string? AttentionEndTime = null,                    // "HH:mm"
    string? ActionsBlock = null,                        // Action Trigger Protocol — bloque "ACCIONES DISPONIBLES" preconstruido por IActionPromptBuilder
    LastActionResult? LastActionResult = null,          // Action Trigger Protocol Fase 4 — resultado de acción previa para inyectar al prompt
    List<ReferenceDocument>? ReferenceDocuments = null, // PDFs del maestro de campaña que el agente usa como contexto
    string? ReferenceDocumentsBlock = null,             // Bloque "DOCUMENTOS DE REFERENCIA" (texto) con las 8 reglas; se concatena al final del system prompt
    /// <summary>
    /// Cuando es true, el runner añade una directiva POST_CHAIN al final del system
    /// prompt para indicar al LLM que una acción ya se ejecutó y debe redactar la
    /// respuesta final al cliente USANDO el LastActionResult, sin emitir nuevos
    /// [ACTION:...]. Se usa para el patrón "tool use" estándar: el primer turno
    /// emite la acción + texto preliminar, y el handler re-invoca con este flag
    /// para obtener la respuesta natural respondiendo a la pregunta original.
    /// </summary>
    bool PostChainRegeneration = false,
    /// <summary>
    /// Motor de flujos — Fase 3. Bloque "## FLUJO ACTIVO" precompilado por IWorkflowPromptBuilder:
    /// paso actual del flujo + datos ya recolectados + transiciones válidas + reglas duras. Le da al
    /// LLM su "guion con estás aquí". Se inyecta entre "## Contexto del cliente" y "## RESULTADO DE
    /// ACCIÓN EJECUTADA". NULL/vacío = la conversación no tiene flujo activo → el prompt queda idéntico
    /// al histórico (mismo patrón condicional que ActionsBlock / ReferenceDocumentsBlock).
    /// </summary>
    string? WorkflowBlock = null
);

/// <summary>
/// Documento de referencia (PDF) que se inyecta al prompt del agente.
/// Se descarga de BlobUrl, se convierte a base64 y se adjunta como DocumentContent
/// al inicio del array de mensajes — Claude lo tiene en contexto durante todo el turno.
/// La Description orienta al agente sobre cuándo consultar este documento.
/// </summary>
public record ReferenceDocument(string FileName, string BlobUrl, string? Description = null);

public record AgentResponse(
    string ReplyText,
    string DetectedIntent,     // cobros | reclamos | renovaciones | humano | cierre
    double ConfidenceScore,
    bool ShouldEscalate,
    bool ShouldClose,
    int TokensUsed
);

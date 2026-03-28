namespace AgentFlow.Domain.Enums;

/// <summary>
/// Proveedor de LLM que usa el tenant para sus agentes IA.
/// Cada proveedor tiene su propia API y modelos disponibles.
/// </summary>
public enum LlmProviderType
{
    /// <summary>Anthropic Claude — modelo principal del sistema</summary>
    Anthropic,

    /// <summary>OpenAI (GPT-4o, etc.) — alternativa</summary>
    OpenAI,

    /// <summary>Google Gemini — alternativa</summary>
    Gemini
}

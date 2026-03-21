namespace AgentFlow.Domain.Entities;

public class AgentTemplate
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    // Prompt e identidad
    public string SystemPrompt { get; set; } = string.Empty;
    public string? Tone { get; set; }
    public string Language { get; set; } = "es";
    public string? AvatarName { get; set; }

    // Comportamiento
    public string? SendFrom { get; set; }
    public string? SendUntil { get; set; }
    public int MaxRetries { get; set; } = 3;
    public int RetryIntervalHours { get; set; } = 24;
    public int InactivityCloseHours { get; set; } = 72;
    public string? CloseConditionKeyword { get; set; }

    // LLM config
    public string LlmModel { get; set; } = "claude-sonnet-4-6";
    public double Temperature { get; set; } = 0.3;
    public int MaxTokens { get; set; } = 1024;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

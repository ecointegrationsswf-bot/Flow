namespace AgentFlow.Domain.Entities;

public class PromptTemplate
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? CategoryId { get; set; }
    public AgentCategory? Category { get; set; }
    public string? SystemPrompt { get; set; }
    public string? ResultPrompt { get; set; }
    public string? AnalysisPrompts { get; set; }
    public string? FieldMapping { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}

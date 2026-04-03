namespace CADMCPServer.Models;

public sealed class ConversationContext
{
    public string SessionId { get; set; } = "default";
    public string? LastModelId { get; set; }
    public string? LastComponentType { get; set; }
    public Dictionary<string, object?> LastParameters { get; set; } = new();
    public List<string> RecentInputs { get; set; } = new();
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ConversationSnapshot
{
    public string? LastModelId { get; set; }
    public string? LastComponentType { get; set; }
    public Dictionary<string, object?> LastParameters { get; set; } = new();
}

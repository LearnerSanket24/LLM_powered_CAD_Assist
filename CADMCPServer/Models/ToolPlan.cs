namespace CADMCPServer.Models;

public sealed class ToolPlan
{
    public string Source { get; set; } = "rule-based";
    public List<PlannedToolCall> ToolCalls { get; set; } = new();
    public List<string> Assumptions { get; set; } = new();
}

public sealed class PlannedToolCall
{
    public string ToolName { get; set; } = string.Empty;
    public Dictionary<string, object?> Arguments { get; set; } = new();
}

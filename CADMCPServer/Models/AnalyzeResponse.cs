using System.Text.Json.Nodes;

namespace CADMCPServer.Models;

public sealed class AnalyzeResponse
{
    public string Status { get; set; } = "PASS";
    public string SessionId { get; set; } = "default";
    public int AttemptsUsed { get; set; }
    public string? ModelId { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<string> Assumptions { get; set; } = new();
    public List<PlannedToolCall> PlannedTools { get; set; } = new();
    public List<ToolExecutionRecord> ExecutionTrace { get; set; } = new();
    public JsonObject? LastError { get; set; }
    public ConversationSnapshot Context { get; set; } = new();
}

public sealed class ToolExecutionRecord
{
    public int Sequence { get; set; }
    public string ToolName { get; set; } = string.Empty;
    public bool Success { get; set; }
    public JsonObject? RequestArguments { get; set; }
    public JsonObject? Result { get; set; }
    public JsonObject? Error { get; set; }
}

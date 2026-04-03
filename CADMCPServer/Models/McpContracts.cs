using System.Text.Json.Nodes;

namespace CADMCPServer.Models;

public sealed class McpToolRequest
{
    public string ToolName { get; set; } = string.Empty;
    public Dictionary<string, object?> Arguments { get; set; } = new();
}

public sealed class McpToolResponse
{
    public bool Success { get; set; }
    public JsonObject? Result { get; set; }
    public McpError? Error { get; set; }
    public int StatusCode { get; set; }
}

public sealed class McpError
{
    public string Code { get; set; } = "unknown_error";
    public string Message { get; set; } = "Unknown error.";
    public JsonObject? Details { get; set; }
    public bool Recoverable { get; set; }
}

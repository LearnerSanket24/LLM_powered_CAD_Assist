using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace CADMCPServer.Models;

public sealed class McpToolRequest
{
    [JsonPropertyName("tool_name")]
    public string ToolName { get; set; } = string.Empty;

    [JsonPropertyName("arguments")]
    public Dictionary<string, object?> Arguments { get; set; } = new();
}

public sealed class McpToolResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("result")]
    public JsonObject? Result { get; set; }

    [JsonPropertyName("error")]
    public McpError? Error { get; set; }

    [JsonIgnore]
    public int StatusCode { get; set; }
}

public sealed class McpError
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = "unknown_error";

    [JsonPropertyName("message")]
    public string Message { get; set; } = "Unknown error.";

    [JsonPropertyName("details")]
    public JsonObject? Details { get; set; }

    [JsonPropertyName("recoverable")]
    public bool Recoverable { get; set; }
}

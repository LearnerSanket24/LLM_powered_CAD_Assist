using System.Text.Json;

namespace CADMCPServer.Models;

public sealed class AnalyzeRequest
{
    public string SessionId { get; set; } = "default";
    public string UserInput { get; set; } = string.Empty;
    public string? ModelId { get; set; }
    public Dictionary<string, JsonElement>? Overrides { get; set; }
}

namespace CADMCPServer.Configuration;

public sealed class McpSettings
{
    public const string SectionName = "Mcp";

    public string BaseUrl { get; set; } = "http://localhost:5000";
    public string ToolRoute { get; set; } = "/mcp/tool";
    public int TimeoutSeconds { get; set; } = 60;
    public bool UseMockResponses { get; set; } = true;
}

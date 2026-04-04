using CADMCPServer.Models;

namespace CADMCPServer.Services.Mcp;

public interface IMcpToolDispatcher
{
    McpToolResponse Execute(McpToolRequest request);
    object GetSchemas();
}

using CADMCPServer.Models;

namespace CADMCPServer.Services.Mcp;

public interface IMcpClient
{
    Task<McpToolResponse> ExecuteToolAsync(McpToolRequest request, CancellationToken cancellationToken);
}

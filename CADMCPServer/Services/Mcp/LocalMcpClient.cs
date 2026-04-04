using CADMCPServer.Models;

namespace CADMCPServer.Services.Mcp;

public sealed class LocalMcpClient : IMcpClient
{
    private readonly IMcpToolDispatcher _dispatcher;

    public LocalMcpClient(IMcpToolDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public Task<McpToolResponse> ExecuteToolAsync(McpToolRequest request, CancellationToken cancellationToken)
    {
        var response = _dispatcher.Execute(request);
        return Task.FromResult(response);
    }
}

using CADMCPServer.Models;

namespace CADMCPServer.Services.Assistant;

public interface IAssistantOrchestrator
{
    Task<AnalyzeResponse> AnalyzeAsync(AnalyzeRequest request, CancellationToken cancellationToken);
}

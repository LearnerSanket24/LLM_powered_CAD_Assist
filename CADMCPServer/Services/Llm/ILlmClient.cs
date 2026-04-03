using CADMCPServer.Models;

namespace CADMCPServer.Services.Llm;

public interface ILlmClient
{
    Task<ToolPlan?> TryBuildPlanAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken);
}

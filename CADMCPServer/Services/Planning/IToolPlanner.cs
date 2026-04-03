using System.Text.Json;
using CADMCPServer.Models;

namespace CADMCPServer.Services.Planning;

public interface IToolPlanner
{
    Task<ToolPlan> BuildPlanAsync(
        string userInput,
        ConversationContext context,
        Dictionary<string, JsonElement>? overrides,
        CancellationToken cancellationToken);

    Task<ToolPlan> ReplanAfterErrorAsync(
        string userInput,
        ConversationContext context,
        ToolPlan previousPlan,
        McpError error,
        int attemptNumber,
        CancellationToken cancellationToken);
}

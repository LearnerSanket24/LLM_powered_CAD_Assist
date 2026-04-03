using System.Text.Json.Nodes;
using CADMCPServer.Models;
using CADMCPServer.Services.Conversation;
using CADMCPServer.Services.Mcp;
using CADMCPServer.Services.Planning;

namespace CADMCPServer.Services.Assistant;

public sealed class AssistantOrchestrator : IAssistantOrchestrator
{
    private const int MaxRetries = 3;

    private readonly IToolPlanner _toolPlanner;
    private readonly IMcpClient _mcpClient;
    private readonly IConversationStore _conversationStore;
    private readonly ILogger<AssistantOrchestrator> _logger;

    public AssistantOrchestrator(
        IToolPlanner toolPlanner,
        IMcpClient mcpClient,
        IConversationStore conversationStore,
        ILogger<AssistantOrchestrator> logger)
    {
        _toolPlanner = toolPlanner;
        _mcpClient = mcpClient;
        _conversationStore = conversationStore;
        _logger = logger;
    }

    public async Task<AnalyzeResponse> AnalyzeAsync(AnalyzeRequest request, CancellationToken cancellationToken)
    {
        var context = _conversationStore.GetOrCreate(request.SessionId);
        var input = request.UserInput?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(input))
        {
            return new AnalyzeResponse
            {
                Status = "FAIL",
                SessionId = context.SessionId,
                AttemptsUsed = 0,
                Message = "UserInput is required.",
                Context = Snapshot(context)
            };
        }

        if (!string.IsNullOrWhiteSpace(request.ModelId))
        {
            context.LastModelId = request.ModelId;
        }

        context.RecentInputs.Add(input);
        if (context.RecentInputs.Count > 20)
        {
            context.RecentInputs.RemoveAt(0);
        }

        var consolidatedTrace = new List<ToolExecutionRecord>();
        var assumptions = new List<string>();
        ToolPlan plan = await _toolPlanner.BuildPlanAsync(input, context, request.Overrides, cancellationToken);
        assumptions.AddRange(plan.Assumptions);

        McpError? lastError = null;
        int attemptsUsed = 0;

        for (var attempt = 1; attempt <= MaxRetries; attempt++)
        {
            attemptsUsed = attempt;
            var attemptTrace = new List<ToolExecutionRecord>();
            var recoverableError = false;

            for (var i = 0; i < plan.ToolCalls.Count; i++)
            {
                var call = CloneCall(plan.ToolCalls[i]);
                HydrateModelId(call, context.LastModelId);

                var requestEnvelope = new McpToolRequest
                {
                    ToolName = call.ToolName,
                    Arguments = call.Arguments
                };

                var mcpResponse = await _mcpClient.ExecuteToolAsync(requestEnvelope, cancellationToken);

                attemptTrace.Add(new ToolExecutionRecord
                {
                    Sequence = consolidatedTrace.Count + attemptTrace.Count + 1,
                    ToolName = call.ToolName,
                    Success = mcpResponse.Success,
                    RequestArguments = JsonObject.Create(call.Arguments),
                    Result = mcpResponse.Result,
                    Error = mcpResponse.Error is null
                        ? null
                        : new JsonObject
                        {
                            ["code"] = mcpResponse.Error.Code,
                            ["message"] = mcpResponse.Error.Message,
                            ["recoverable"] = mcpResponse.Error.Recoverable,
                            ["details"] = mcpResponse.Error.Details
                        }
                });

                if (mcpResponse.Success)
                {
                    var returnedModelId = mcpResponse.Result?["model_id"]?.GetValue<string>();
                    if (!string.IsNullOrWhiteSpace(returnedModelId))
                    {
                        context.LastModelId = returnedModelId;
                    }

                    UpdateContextFromCall(context, call);
                    continue;
                }

                lastError = mcpResponse.Error ?? new McpError
                {
                    Code = "unknown_mcp_error",
                    Message = "MCP tool call failed.",
                    Recoverable = false
                };

                if (IsRecoverable(lastError) && attempt < MaxRetries)
                {
                    recoverableError = true;
                    break;
                }

                consolidatedTrace.AddRange(attemptTrace);
                context.UpdatedAt = DateTimeOffset.UtcNow;
                _conversationStore.Save(context);

                return BuildFailureResponse(context, assumptions, plan, consolidatedTrace, attemptsUsed, lastError);
            }

            consolidatedTrace.AddRange(attemptTrace);

            if (!recoverableError)
            {
                context.UpdatedAt = DateTimeOffset.UtcNow;
                _conversationStore.Save(context);
                return BuildSuccessResponse(context, assumptions, plan, consolidatedTrace, attemptsUsed);
            }

            if (lastError is not null)
            {
                _logger.LogInformation(
                    "Recoverable CAD error detected on attempt {Attempt}. Replanning. Error: {ErrorCode}",
                    attempt,
                    lastError.Code);

                plan = await _toolPlanner.ReplanAfterErrorAsync(input, context, plan, lastError, attempt + 1, cancellationToken);
                assumptions.AddRange(plan.Assumptions);
            }
        }

        context.UpdatedAt = DateTimeOffset.UtcNow;
        _conversationStore.Save(context);

        return BuildFailureResponse(
            context,
            assumptions,
            plan,
            consolidatedTrace,
            attemptsUsed,
            lastError ?? new McpError
            {
                Code = "retry_exhausted",
                Message = "Self-correction loop exhausted all retries.",
                Recoverable = false
            });
    }

    private static AnalyzeResponse BuildSuccessResponse(
        ConversationContext context,
        IEnumerable<string> assumptions,
        ToolPlan plan,
        List<ToolExecutionRecord> executionTrace,
        int attemptsUsed)
    {
        var distinctAssumptions = assumptions.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
        var message = distinctAssumptions.Count == 0
            ? "Execution completed successfully."
            : "Execution completed with engineering assumptions. Review assumptions list.";

        return new AnalyzeResponse
        {
            Status = "PASS",
            SessionId = context.SessionId,
            AttemptsUsed = attemptsUsed,
            ModelId = context.LastModelId,
            Message = message,
            Assumptions = distinctAssumptions,
            PlannedTools = plan.ToolCalls,
            ExecutionTrace = executionTrace,
            Context = Snapshot(context)
        };
    }

    private static AnalyzeResponse BuildFailureResponse(
        ConversationContext context,
        IEnumerable<string> assumptions,
        ToolPlan plan,
        List<ToolExecutionRecord> executionTrace,
        int attemptsUsed,
        McpError error)
    {
        return new AnalyzeResponse
        {
            Status = "FAIL",
            SessionId = context.SessionId,
            AttemptsUsed = attemptsUsed,
            ModelId = context.LastModelId,
            Message = error.Message,
            Assumptions = assumptions.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList(),
            PlannedTools = plan.ToolCalls,
            ExecutionTrace = executionTrace,
            LastError = new JsonObject
            {
                ["code"] = error.Code,
                ["message"] = error.Message,
                ["recoverable"] = error.Recoverable,
                ["details"] = error.Details
            },
            Context = Snapshot(context)
        };
    }

    private static PlannedToolCall CloneCall(PlannedToolCall call)
    {
        return new PlannedToolCall
        {
            ToolName = call.ToolName,
            Arguments = new Dictionary<string, object?>(call.Arguments, StringComparer.OrdinalIgnoreCase)
        };
    }

    private static void HydrateModelId(PlannedToolCall call, string? currentModelId)
    {
        if (call.Arguments.TryGetValue("model_id", out var placeholderObj) && placeholderObj is string placeholder)
        {
            if (placeholder.Equals("$LATEST_MODEL_ID", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(currentModelId))
            {
                call.Arguments["model_id"] = currentModelId;
            }
        }

        if (call.ToolName.StartsWith("create_", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!call.Arguments.ContainsKey("model_id") && !string.IsNullOrWhiteSpace(currentModelId))
        {
            call.Arguments["model_id"] = currentModelId;
        }
    }

    private static bool IsRecoverable(McpError error)
    {
        if (error.Recoverable)
        {
            return true;
        }

        var text = $"{error.Code} {error.Message}".ToLowerInvariant();
        return text.Contains("eyeshot") || text.Contains("invalid geometry") || text.Contains("constraint") || text.Contains("fillet") || text.Contains("chamfer");
    }

    private static void UpdateContextFromCall(ConversationContext context, PlannedToolCall call)
    {
        if (call.ToolName.StartsWith("create_", StringComparison.OrdinalIgnoreCase))
        {
            context.LastComponentType = call.ToolName.Replace("create_", string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        foreach (var pair in call.Arguments)
        {
            if (string.Equals(pair.Key, "model_id", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            context.LastParameters[pair.Key] = pair.Value;
        }
    }

    private static ConversationSnapshot Snapshot(ConversationContext context)
    {
        return new ConversationSnapshot
        {
            LastModelId = context.LastModelId,
            LastComponentType = context.LastComponentType,
            LastParameters = new Dictionary<string, object?>(context.LastParameters, StringComparer.OrdinalIgnoreCase)
        };
    }
}

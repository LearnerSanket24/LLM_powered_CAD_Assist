using System.Text.Json.Nodes;
using System.Text.Json;
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
    private readonly ISmartCadAnalyzer _smartCadAnalyzer;
    private readonly ILogger<AssistantOrchestrator> _logger;

    public AssistantOrchestrator(
        IToolPlanner toolPlanner,
        IMcpClient mcpClient,
        IConversationStore conversationStore,
        ISmartCadAnalyzer smartCadAnalyzer,
        ILogger<AssistantOrchestrator> logger)
    {
        _toolPlanner = toolPlanner;
        _mcpClient = mcpClient;
        _conversationStore = conversationStore;
        _smartCadAnalyzer = smartCadAnalyzer;
        _logger = logger;
    }

    public async Task<AnalyzeResponse> AnalyzeAsync(AnalyzeRequest request, CancellationToken cancellationToken)
    {
        var context = _conversationStore.GetOrCreate(request.SessionId);
        var input = request.UserInput?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(input))
        {
            var analysis = new SmartCadAnalysis
            {
                ComponentType = string.IsNullOrWhiteSpace(request.ComponentType) ? "generic" : request.ComponentType.Trim().ToLowerInvariant(),
                Status = "FAIL",
                Recommendations = new List<string>
                {
                    "1. Provide a non-empty natural language command in userInput."
                },
                AssumptionsUsed = new List<string>
                {
                    "userInput was empty."
                }
            };

            return new AnalyzeResponse
            {
                Status = "FAIL",
                SessionId = context.SessionId,
                AttemptsUsed = 0,
                Message = _smartCadAnalyzer.Format(analysis),
                Assumptions = analysis.AssumptionsUsed,
                Context = Snapshot(context),
                Analysis = analysis
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
        ApplyRequestPreferences(plan, request);
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
                HydrateSessionContext(call, context.SessionId);

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
                    RequestArguments = JsonSerializer.SerializeToNode(call.Arguments) as JsonObject,
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

                return BuildFailureResponse(request, context, assumptions, plan, consolidatedTrace, attemptsUsed, lastError, _smartCadAnalyzer);
            }

            consolidatedTrace.AddRange(attemptTrace);

            if (!recoverableError)
            {
                context.UpdatedAt = DateTimeOffset.UtcNow;
                _conversationStore.Save(context);
                return BuildSuccessResponse(request, context, assumptions, plan, consolidatedTrace, attemptsUsed, _smartCadAnalyzer);
            }

            if (lastError is not null)
            {
                _logger.LogInformation(
                    "Recoverable CAD error detected on attempt {Attempt}. Replanning. Error: {ErrorCode}",
                    attempt,
                    lastError.Code);

                plan = await _toolPlanner.ReplanAfterErrorAsync(input, context, plan, lastError, attempt + 1, cancellationToken);
                ApplyRequestPreferences(plan, request);
                assumptions.AddRange(plan.Assumptions);
            }
        }

        context.UpdatedAt = DateTimeOffset.UtcNow;
        _conversationStore.Save(context);

        return BuildFailureResponse(
            request,
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
            },
            _smartCadAnalyzer);
    }

    private static AnalyzeResponse BuildSuccessResponse(
        AnalyzeRequest request,
        ConversationContext context,
        IEnumerable<string> assumptions,
        ToolPlan plan,
        List<ToolExecutionRecord> executionTrace,
        int attemptsUsed,
        ISmartCadAnalyzer smartCadAnalyzer)
    {
        var distinctAssumptions = assumptions.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
        var analysis = smartCadAnalyzer.Analyze(request, context, distinctAssumptions);
        var message = smartCadAnalyzer.Format(analysis);

        return new AnalyzeResponse
        {
            Status = analysis.Status,
            SessionId = context.SessionId,
            AttemptsUsed = attemptsUsed,
            ModelId = context.LastModelId,
            Message = message,
            Assumptions = distinctAssumptions,
            PlannedTools = plan.ToolCalls,
            ExecutionTrace = executionTrace,
            Context = Snapshot(context),
            Analysis = analysis
        };
    }

    private static AnalyzeResponse BuildFailureResponse(
        AnalyzeRequest request,
        ConversationContext context,
        IEnumerable<string> assumptions,
        ToolPlan plan,
        List<ToolExecutionRecord> executionTrace,
        int attemptsUsed,
        McpError error,
        ISmartCadAnalyzer smartCadAnalyzer)
    {
        var distinctAssumptions = assumptions.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
        var analysis = smartCadAnalyzer.Analyze(request, context, distinctAssumptions);
        analysis.Status = "FAIL";
        analysis.Recommendations.Insert(0, "1. Resolve CAD execution failure before finalizing manufacturing decisions.");
        RenumberRecommendations(analysis.Recommendations);

        return new AnalyzeResponse
        {
            Status = "FAIL",
            SessionId = context.SessionId,
            AttemptsUsed = attemptsUsed,
            ModelId = context.LastModelId,
            Message = smartCadAnalyzer.Format(analysis),
            Assumptions = distinctAssumptions,
            PlannedTools = plan.ToolCalls,
            ExecutionTrace = executionTrace,
            LastError = new JsonObject
            {
                ["code"] = error.Code,
                ["message"] = error.Message,
                ["recoverable"] = error.Recoverable,
                ["details"] = error.Details
            },
            Context = Snapshot(context),
            Analysis = analysis
        };
    }

    private static void RenumberRecommendations(List<string> recommendations)
    {
        for (var i = 0; i < recommendations.Count; i++)
        {
            var current = recommendations[i] ?? string.Empty;
            var dotIndex = current.IndexOf('.');
            var cleaned = dotIndex > -1 && dotIndex + 1 < current.Length
                ? current[(dotIndex + 1)..].TrimStart()
                : current;

            recommendations[i] = $"{i + 1}. {cleaned}";
        }
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

    private static void HydrateSessionContext(PlannedToolCall call, string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        if (!call.Arguments.ContainsKey("session_id"))
        {
            call.Arguments["session_id"] = sessionId;
        }
    }

    private static void ApplyRequestPreferences(ToolPlan plan, AnalyzeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Material))
        {
            return;
        }

        foreach (var call in plan.ToolCalls)
        {
            if (call.ToolName.StartsWith("create_", StringComparison.OrdinalIgnoreCase))
            {
                call.Arguments["material"] = request.Material.Trim();
            }
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

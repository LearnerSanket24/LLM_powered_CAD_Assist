using System.Globalization;
using System.Text.Json;
using CADMCPServer.Models;
using CADMCPServer.Services.Llm;

namespace CADMCPServer.Services.Planning;

public sealed class ToolPlanner : IToolPlanner
{
    private const string PlannerSystemPrompt = @"You are a CAD MCP planning agent.
Your task is to convert plain-English CAD requests into ordered MCP tool calls.

Rules:
1) Return a sequence of tool calls in execution order.
2) Use only supported tool names.
3) If user omits required dimensions, choose sane engineering defaults and list each as an assumption.
4) If request modifies existing model, include model_id when available from context.
5) After any creation call (create_gear/create_shaft/create_bearing), include get_volume, get_mass, get_surface_area.
6) Return only function arguments for plan_cad_tools.";

    private readonly ILlmClient _llmClient;
    private readonly ILogger<ToolPlanner> _logger;

    public ToolPlanner(ILlmClient llmClient, ILogger<ToolPlanner> logger)
    {
        _llmClient = llmClient;
        _logger = logger;
    }

    public async Task<ToolPlan> BuildPlanAsync(
        string userInput,
        ConversationContext context,
        Dictionary<string, JsonElement>? overrides,
        CancellationToken cancellationToken)
    {
        var contextSummary = BuildContextSummary(context);
        var llmPrompt = $@"User input:
    {userInput}

    Conversation context:
    {contextSummary}";

        var llmPlan = await _llmClient.TryBuildPlanAsync(PlannerSystemPrompt, llmPrompt, cancellationToken);
        if (llmPlan is not null)
        {
            var normalized = NormalizePlan(llmPlan, userInput, context, overrides);
            if (normalized.ToolCalls.Count > 0)
            {
                return normalized;
            }
        }

        return BuildRuleBasedPlan(userInput, context, overrides);
    }

    public async Task<ToolPlan> ReplanAfterErrorAsync(
        string userInput,
        ConversationContext context,
        ToolPlan previousPlan,
        McpError error,
        int attemptNumber,
        CancellationToken cancellationToken)
    {
        var previousJson = JsonSerializer.Serialize(previousPlan);
        var errorJson = JsonSerializer.Serialize(error);

        var llmPrompt = $@"Original user input:
    {userInput}

    Previous tool plan:
    {previousJson}

    Execution error:
    {errorJson}

    Retry attempt:
    {attemptNumber}

    Build a safer revised plan.";

        var llmPlan = await _llmClient.TryBuildPlanAsync(PlannerSystemPrompt, llmPrompt, cancellationToken);
        if (llmPlan is not null)
        {
            var normalized = NormalizePlan(llmPlan, userInput, context, null);
            normalized.Assumptions.Add($"Retry {attemptNumber}: plan regenerated from CAD error {error.Code}.");
            if (normalized.ToolCalls.Count > 0)
            {
                return normalized;
            }
        }

        _logger.LogInformation("Using deterministic replan for attempt {AttemptNumber} due to error {ErrorCode}", attemptNumber, error.Code);
        return BuildHeuristicRepairPlan(previousPlan, error, attemptNumber, context);
    }

    private ToolPlan BuildRuleBasedPlan(
        string userInput,
        ConversationContext context,
        Dictionary<string, JsonElement>? overrides)
    {
        var plan = new ToolPlan
        {
            Source = "rule-based"
        };

        var input = userInput.Trim();
        var lower = input.ToLowerInvariant();
        var component = DetectComponentType(lower, context.LastComponentType);

        var isCreationRequest = HasAny(lower, "create", "make", "generate", "build");
        var hasModificationVerb = HasAny(lower, "modify", "change", "increase", "decrease", "set", "update", "now");

        if (isCreationRequest && component is not null)
        {
            var createCall = BuildCreateCall(component, input, plan.Assumptions);
            ApplyOverrides(createCall, overrides);
            plan.ToolCalls.Add(createCall);

            if (lower.Contains("fillet"))
            {
                var radius = ExtractDouble(input, @"fillet\s*(?:radius)?\s*(?:to|=)?\s*([0-9]*\.?[0-9]+)") ?? 1.0;
                plan.ToolCalls.Add(new PlannedToolCall
                {
                    ToolName = "add_fillet",
                    Arguments = new Dictionary<string, object?>
                    {
                        ["model_id"] = "$LATEST_MODEL_ID",
                        ["radius"] = radius
                    }
                });
            }

            if (lower.Contains("chamfer"))
            {
                var distance = ExtractDouble(input, @"chamfer\s*(?:distance)?\s*(?:to|=)?\s*([0-9]*\.?[0-9]+)") ?? 1.0;
                plan.ToolCalls.Add(new PlannedToolCall
                {
                    ToolName = "add_chamfer",
                    Arguments = new Dictionary<string, object?>
                    {
                        ["model_id"] = "$LATEST_MODEL_ID",
                        ["distance"] = distance
                    }
                });
            }
        }
        else
        {
            if (hasModificationVerb)
            {
                foreach (var dimChangeCall in BuildDimensionChanges(input, context))
                {
                    ApplyOverrides(dimChangeCall, overrides);
                    plan.ToolCalls.Add(dimChangeCall);
                }
            }

            if (lower.Contains("fillet"))
            {
                plan.ToolCalls.Add(new PlannedToolCall
                {
                    ToolName = "add_fillet",
                    Arguments = new Dictionary<string, object?>
                    {
                        ["model_id"] = context.LastModelId ?? string.Empty,
                        ["radius"] = ExtractDouble(input, @"fillet\s*(?:radius)?\s*(?:to|=)?\s*([0-9]*\.?[0-9]+)") ?? 1.0
                    }
                });
            }

            if (lower.Contains("chamfer"))
            {
                plan.ToolCalls.Add(new PlannedToolCall
                {
                    ToolName = "add_chamfer",
                    Arguments = new Dictionary<string, object?>
                    {
                        ["model_id"] = context.LastModelId ?? string.Empty,
                        ["distance"] = ExtractDouble(input, @"chamfer\s*(?:distance)?\s*(?:to|=)?\s*([0-9]*\.?[0-9]+)") ?? 1.0
                    }
                });
            }

            if (lower.Contains("volume"))
            {
                plan.ToolCalls.Add(SimpleModelTool("get_volume", context.LastModelId));
            }

            if (lower.Contains("mass"))
            {
                plan.ToolCalls.Add(SimpleModelTool("get_mass", context.LastModelId));
            }

            if (lower.Contains("surface area") || lower.Contains("area"))
            {
                plan.ToolCalls.Add(SimpleModelTool("get_surface_area", context.LastModelId));
            }

            if (lower.Contains("clearance"))
            {
                plan.ToolCalls.Add(SimpleModelTool("measure_clearance", context.LastModelId));
            }

            if (lower.Contains("interference") || lower.Contains("collision"))
            {
                plan.ToolCalls.Add(SimpleModelTool("check_interference", context.LastModelId));
            }

            if (lower.Contains("export") && lower.Contains("step"))
            {
                plan.ToolCalls.Add(SimpleModelTool("export_step", context.LastModelId));
            }

            if (lower.Contains("export") && lower.Contains("stl"))
            {
                plan.ToolCalls.Add(SimpleModelTool("export_stl", context.LastModelId));
            }

            if (lower.Contains("render") || lower.Contains("viewport") || lower.Contains("preview"))
            {
                plan.ToolCalls.Add(SimpleModelTool("render_viewport", context.LastModelId));
            }
        }

        if (plan.ToolCalls.Count == 0)
        {
            var fallback = BuildCreateCall(component ?? "gear", input, plan.Assumptions);
            plan.Assumptions.Add("Input was ambiguous, defaulted to creating a gear-based starter model.");
            ApplyOverrides(fallback, overrides);
            plan.ToolCalls.Add(fallback);
        }

        AttachModelIds(plan, context.LastModelId);
        EnsureAutoQueriesAfterCreation(plan);

        if (context.LastModelId is null && plan.ToolCalls.Any(call => !call.ToolName.StartsWith("create_", StringComparison.OrdinalIgnoreCase)))
        {
            plan.Assumptions.Add("No active model_id found in session. Non-creation tools will require model_id from caller or backend context.");
        }

        plan.Assumptions = plan.Assumptions.Distinct().ToList();
        return plan;
    }

    private ToolPlan NormalizePlan(
        ToolPlan plan,
        string userInput,
        ConversationContext context,
        Dictionary<string, JsonElement>? overrides)
    {
        var normalized = new ToolPlan
        {
            Source = plan.Source,
            Assumptions = plan.Assumptions.ToList()
        };

        foreach (var call in plan.ToolCalls)
        {
            if (string.IsNullOrWhiteSpace(call.ToolName))
            {
                continue;
            }

            var cloned = new PlannedToolCall
            {
                ToolName = call.ToolName.Trim(),
                Arguments = new Dictionary<string, object?>(call.Arguments, StringComparer.OrdinalIgnoreCase)
            };

            ApplyOverrides(cloned, overrides);
            normalized.ToolCalls.Add(cloned);
        }

        if (normalized.ToolCalls.Count == 0)
        {
            return BuildRuleBasedPlan(userInput, context, overrides);
        }

        AttachModelIds(normalized, context.LastModelId);
        EnsureAutoQueriesAfterCreation(normalized);
        return normalized;
    }

    private static void AttachModelIds(ToolPlan plan, string? lastModelId)
    {
        foreach (var call in plan.ToolCalls)
        {
            if (call.ToolName.StartsWith("create_", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!call.Arguments.ContainsKey("model_id") && !string.IsNullOrWhiteSpace(lastModelId))
            {
                call.Arguments["model_id"] = lastModelId;
            }
        }
    }

    private static void EnsureAutoQueriesAfterCreation(ToolPlan plan)
    {
        var hasCreation = plan.ToolCalls.Any(call => call.ToolName.StartsWith("create_", StringComparison.OrdinalIgnoreCase));
        if (!hasCreation)
        {
            return;
        }

        var existing = new HashSet<string>(plan.ToolCalls.Select(call => call.ToolName), StringComparer.OrdinalIgnoreCase);
        var required = new[] { "get_volume", "get_mass", "get_surface_area" };

        foreach (var queryTool in required)
        {
            if (existing.Contains(queryTool))
            {
                continue;
            }

            plan.ToolCalls.Add(new PlannedToolCall
            {
                ToolName = queryTool,
                Arguments = new Dictionary<string, object?>
                {
                    ["model_id"] = "$LATEST_MODEL_ID"
                }
            });
        }
    }

    private static PlannedToolCall BuildCreateCall(string component, string userInput, ICollection<string> assumptions)
    {
        return component switch
        {
            "gear" => BuildCreateGearCall(userInput, assumptions),
            "shaft" => BuildCreateShaftCall(userInput, assumptions),
            "bearing" => BuildCreateBearingCall(userInput, assumptions),
            _ => BuildCreateGearCall(userInput, assumptions)
        };
    }

    private static PlannedToolCall BuildCreateGearCall(string input, ICollection<string> assumptions)
    {
        var teeth = ExtractInt(input, @"([0-9]+)\s*(?:teeth|tooth)") ?? 20;
        var module = ExtractDouble(input, @"module\s*(?:to|=)?\s*([0-9]*\.?[0-9]+)") ?? 2.0;
        var pressureAngle = ExtractDouble(input, @"pressure\s*angle\s*(?:to|=)?\s*([0-9]*\.?[0-9]+)") ?? 20.0;
        var faceWidth =
            ExtractDouble(input, @"face\s*width\s*(?:to|=)?\s*([0-9]*\.?[0-9]+)")
            ?? ExtractDouble(input, @"width\s*(?:to|=)?\s*([0-9]*\.?[0-9]+)")
            ?? 20.0;
        var boreDia =
            ExtractDouble(input, @"bore\s*(?:dia|diameter)?\s*(?:to|=)?\s*([0-9]*\.?[0-9]+)")
            ?? Math.Max(1.0, (teeth * module) * 0.2);

        if (!RegexIsMatch(input, @"([0-9]+)\s*(?:teeth|tooth)"))
        {
            assumptions.Add("Missing gear teeth. Assumed teeth=20.");
        }

        if (!RegexIsMatch(input, @"module\s*(?:to|=)?\s*([0-9]*\.?[0-9]+)"))
        {
            assumptions.Add("Missing module. Assumed module=2.0 mm.");
        }

        if (!RegexIsMatch(input, @"(face\s*width|width)\s*(?:to|=)?\s*([0-9]*\.?[0-9]+)"))
        {
            assumptions.Add("Missing face width. Assumed face_width=20 mm.");
        }

        if (!RegexIsMatch(input, @"bore\s*(?:dia|diameter)?\s*(?:to|=)?\s*([0-9]*\.?[0-9]+)"))
        {
            assumptions.Add($"Missing bore diameter. Assumed bore_dia={Math.Round(boreDia, 2)} mm.");
        }

        return new PlannedToolCall
        {
            ToolName = "create_gear",
            Arguments = new Dictionary<string, object?>
            {
                ["teeth"] = Math.Max(8, teeth),
                ["module"] = Math.Max(0.5, module),
                ["face_width"] = Math.Max(2.0, faceWidth),
                ["pressure_angle"] = Math.Clamp(pressureAngle, 10.0, 35.0),
                ["bore_dia"] = Math.Max(0.5, boreDia),
                ["material"] = "Mild Steel"
            }
        };
    }

    private static PlannedToolCall BuildCreateShaftCall(string input, ICollection<string> assumptions)
    {
        var length = ExtractDouble(input, @"length\s*(?:to|=)?\s*([0-9]*\.?[0-9]+)") ?? 100.0;
        var diameter = ExtractDouble(input, @"diameter\s*(?:to|=)?\s*([0-9]*\.?[0-9]+)") ?? 20.0;
        var material = ExtractMaterial(input) ?? "Mild Steel";

        if (!RegexIsMatch(input, @"length\s*(?:to|=)?\s*([0-9]*\.?[0-9]+)"))
        {
            assumptions.Add("Missing shaft length. Assumed length=100 mm.");
        }

        if (!RegexIsMatch(input, @"diameter\s*(?:to|=)?\s*([0-9]*\.?[0-9]+)"))
        {
            assumptions.Add("Missing shaft diameter. Assumed diameter=20 mm.");
        }

        if (ExtractMaterial(input) is null)
        {
            assumptions.Add("Missing shaft material. Assumed material=Mild Steel.");
        }

        return new PlannedToolCall
        {
            ToolName = "create_shaft",
            Arguments = new Dictionary<string, object?>
            {
                ["length"] = Math.Max(10.0, length),
                ["diameter"] = Math.Max(2.0, diameter),
                ["material"] = material
            }
        };
    }

    private static PlannedToolCall BuildCreateBearingCall(string input, ICollection<string> assumptions)
    {
        var inner = ExtractDouble(input, @"inner\s*(?:diameter|dia)\s*(?:to|=)?\s*([0-9]*\.?[0-9]+)") ?? 20.0;
        var outer = ExtractDouble(input, @"outer\s*(?:diameter|dia)\s*(?:to|=)?\s*([0-9]*\.?[0-9]+)") ?? 47.0;
        var width = ExtractDouble(input, @"width\s*(?:to|=)?\s*([0-9]*\.?[0-9]+)") ?? 14.0;

        if (!RegexIsMatch(input, @"inner\s*(?:diameter|dia)\s*(?:to|=)?\s*([0-9]*\.?[0-9]+)"))
        {
            assumptions.Add("Missing bearing inner diameter. Assumed inner_diameter=20 mm.");
        }

        if (!RegexIsMatch(input, @"outer\s*(?:diameter|dia)\s*(?:to|=)?\s*([0-9]*\.?[0-9]+)"))
        {
            assumptions.Add("Missing bearing outer diameter. Assumed outer_diameter=47 mm.");
        }

        if (!RegexIsMatch(input, @"width\s*(?:to|=)?\s*([0-9]*\.?[0-9]+)"))
        {
            assumptions.Add("Missing bearing width. Assumed width=14 mm.");
        }

        if (outer <= inner)
        {
            outer = inner + 10;
            assumptions.Add("Outer diameter must be larger than inner diameter; adjusted outer_diameter.");
        }

        return new PlannedToolCall
        {
            ToolName = "create_bearing",
            Arguments = new Dictionary<string, object?>
            {
                ["inner_diameter"] = Math.Max(2.0, inner),
                ["outer_diameter"] = Math.Max(3.0, outer),
                ["width"] = Math.Max(2.0, width)
            }
        };
    }

    private static IEnumerable<PlannedToolCall> BuildDimensionChanges(string input, ConversationContext context)
    {
        var updates = new List<PlannedToolCall>();

        var module = ExtractDouble(input, @"module\s*(?:to|=)?\s*([0-9]*\.?[0-9]+)");
        if (module.HasValue)
        {
            updates.Add(BuildModifyDimCall(context.LastModelId, "module", Math.Max(0.5, module.Value)));
        }

        var teeth = ExtractInt(input, @"([0-9]+)\s*(?:teeth|tooth)");
        if (teeth.HasValue)
        {
            updates.Add(BuildModifyDimCall(context.LastModelId, "teeth", Math.Max(8, teeth.Value)));
        }

        var faceWidth =
            ExtractDouble(input, @"face\s*width\s*(?:to|=)?\s*([0-9]*\.?[0-9]+)")
            ?? ExtractDouble(input, @"width\s*(?:to|=)?\s*([0-9]*\.?[0-9]+)");
        if (faceWidth.HasValue)
        {
            updates.Add(BuildModifyDimCall(context.LastModelId, "face_width", Math.Max(2.0, faceWidth.Value)));
        }

        var length = ExtractDouble(input, @"length\s*(?:to|=)?\s*([0-9]*\.?[0-9]+)");
        if (length.HasValue)
        {
            updates.Add(BuildModifyDimCall(context.LastModelId, "length", Math.Max(10.0, length.Value)));
        }

        var diameter = ExtractDouble(input, @"diameter\s*(?:to|=)?\s*([0-9]*\.?[0-9]+)");
        if (diameter.HasValue)
        {
            updates.Add(BuildModifyDimCall(context.LastModelId, "diameter", Math.Max(2.0, diameter.Value)));
        }

        return updates;
    }

    private static PlannedToolCall BuildModifyDimCall(string? modelId, string dimensionName, object value)
    {
        var args = new Dictionary<string, object?>
        {
            ["param"] = dimensionName,
            ["value"] = value
        };

        if (!string.IsNullOrWhiteSpace(modelId))
        {
            args["model_id"] = modelId;
        }

        return new PlannedToolCall
        {
            ToolName = "modify_dim",
            Arguments = args
        };
    }

    private static PlannedToolCall SimpleModelTool(string toolName, string? modelId)
    {
        return new PlannedToolCall
        {
            ToolName = toolName,
            Arguments = new Dictionary<string, object?>
            {
                ["model_id"] = modelId ?? string.Empty
            }
        };
    }

    private static ToolPlan BuildHeuristicRepairPlan(ToolPlan previousPlan, McpError error, int attemptNumber, ConversationContext context)
    {
        var repaired = new ToolPlan
        {
            Source = $"heuristic-repair-{attemptNumber}",
            Assumptions = previousPlan.Assumptions.ToList()
        };

        var errorText = $"{error.Code} {error.Message}".ToLowerInvariant();

        foreach (var originalCall in previousPlan.ToolCalls)
        {
            var call = new PlannedToolCall
            {
                ToolName = originalCall.ToolName,
                Arguments = new Dictionary<string, object?>(originalCall.Arguments, StringComparer.OrdinalIgnoreCase)
            };

            if (call.ToolName.Equals("create_gear", StringComparison.OrdinalIgnoreCase) ||
                call.ToolName.Equals("modify_dim", StringComparison.OrdinalIgnoreCase))
            {
                if (errorText.Contains("module"))
                {
                    if (TryGetDouble(call.Arguments, "module", out var moduleValue))
                    {
                        call.Arguments["module"] = Math.Max(0.5, moduleValue * 0.8);
                        repaired.Assumptions.Add("Retry adjustment: reduced module by 20% after geometry error.");
                    }

                    if (TryGetDouble(call.Arguments, "value", out var valueModule) &&
                        string.Equals(call.Arguments.GetValueOrDefault("param")?.ToString(), "module", StringComparison.OrdinalIgnoreCase))
                    {
                        call.Arguments["value"] = Math.Max(0.5, valueModule * 0.8);
                        repaired.Assumptions.Add("Retry adjustment: reduced requested module value.");
                    }
                }

                if (errorText.Contains("teeth"))
                {
                    if (call.Arguments.TryGetValue("teeth", out var teethObj) && int.TryParse(teethObj?.ToString(), out var teeth))
                    {
                        call.Arguments["teeth"] = Math.Max(12, teeth);
                        repaired.Assumptions.Add("Retry adjustment: increased gear teeth to maintain valid geometry.");
                    }
                }
            }

            if (call.ToolName.Equals("add_fillet", StringComparison.OrdinalIgnoreCase) && errorText.Contains("fillet"))
            {
                if (TryGetDouble(call.Arguments, "radius", out var radius))
                {
                    call.Arguments["radius"] = Math.Max(0.2, radius * 0.5);
                    repaired.Assumptions.Add("Retry adjustment: reduced fillet radius by 50%.");
                }
            }

            if (call.ToolName.Equals("add_chamfer", StringComparison.OrdinalIgnoreCase) && errorText.Contains("chamfer"))
            {
                if (TryGetDouble(call.Arguments, "distance", out var distance))
                {
                    call.Arguments["distance"] = Math.Max(0.2, distance * 0.5);
                    repaired.Assumptions.Add("Retry adjustment: reduced chamfer distance by 50%.");
                }
            }

            repaired.ToolCalls.Add(call);
        }

        AttachModelIds(repaired, context.LastModelId);
        EnsureAutoQueriesAfterCreation(repaired);
        repaired.Assumptions = repaired.Assumptions.Distinct().ToList();
        return repaired;
    }

    private static string BuildContextSummary(ConversationContext context)
    {
        return JsonSerializer.Serialize(new
        {
            session_id = context.SessionId,
            last_model_id = context.LastModelId,
            last_component_type = context.LastComponentType,
            last_parameters = context.LastParameters,
            recent_inputs = context.RecentInputs.TakeLast(5).ToArray()
        });
    }

    private static string? DetectComponentType(string lowerInput, string? fallbackComponent)
    {
        if (lowerInput.Contains("gear"))
        {
            return "gear";
        }

        if (lowerInput.Contains("shaft"))
        {
            return "shaft";
        }

        if (lowerInput.Contains("bearing"))
        {
            return "bearing";
        }

        return fallbackComponent;
    }

    private static bool HasAny(string text, params string[] keywords)
    {
        return keywords.Any(text.Contains);
    }

    private static void ApplyOverrides(PlannedToolCall call, Dictionary<string, JsonElement>? overrides)
    {
        if (overrides is null)
        {
            return;
        }

        foreach (var pair in overrides)
        {
            call.Arguments[pair.Key] = ConvertJsonElement(pair.Value);
        }
    }

    private static object? ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt64(out var i) => i,
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonElement).ToList(),
            JsonValueKind.Object => element.EnumerateObject().ToDictionary(v => v.Name, v => ConvertJsonElement(v.Value)),
            _ => element.GetRawText()
        };
    }

    private static bool TryGetDouble(Dictionary<string, object?> source, string key, out double value)
    {
        value = 0;
        if (!source.TryGetValue(key, out var raw) || raw is null)
        {
            return false;
        }

        return double.TryParse(raw.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out value);
    }

    private static double? ExtractDouble(string text, string pattern)
    {
        var match = System.Text.RegularExpressions.Regex.Match(text, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!match.Success || match.Groups.Count < 2)
        {
            return null;
        }

        var valueText = match.Groups[1].Value;
        if (double.TryParse(valueText, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }

        return null;
    }

    private static int? ExtractInt(string text, string pattern)
    {
        var match = System.Text.RegularExpressions.Regex.Match(text, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!match.Success || match.Groups.Count < 2)
        {
            return null;
        }

        return int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static string? ExtractMaterial(string text)
    {
        if (RegexIsMatch(text, @"mild\s+steel"))
        {
            return "Mild Steel";
        }

        if (RegexIsMatch(text, @"carbon\s+steel"))
        {
            return "Carbon Steel";
        }

        if (RegexIsMatch(text, @"alloy\s+steel"))
        {
            return "Alloy Steel";
        }

        return null;
    }

    private static bool RegexIsMatch(string text, string pattern)
    {
        return System.Text.RegularExpressions.Regex.IsMatch(text, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
}

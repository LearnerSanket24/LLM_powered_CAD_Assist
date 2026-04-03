using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CADMCPServer.Configuration;
using CADMCPServer.Models;
using Microsoft.Extensions.Options;

namespace CADMCPServer.Services.Llm;

public sealed class FunctionCallingLlmClient : ILlmClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly HashSet<string> AllowedToolNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "create_gear",
        "create_shaft",
        "create_bearing",
        "modify_dim",
        "add_fillet",
        "add_chamfer",
        "get_volume",
        "get_mass",
        "get_surface_area",
        "measure_clearance",
        "check_interference",
        "export_step",
        "export_stl",
        "render_viewport"
    };

    private readonly HttpClient _httpClient;
    private readonly LlmSettings _settings;
    private readonly ILogger<FunctionCallingLlmClient> _logger;

    public FunctionCallingLlmClient(
        HttpClient httpClient,
        IOptions<LlmSettings> settings,
        ILogger<FunctionCallingLlmClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<ToolPlan?> TryBuildPlanAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
    {
        var provider = (_settings.Provider ?? "none").Trim().ToLowerInvariant();
        if (provider is "none" or "off" or "disabled")
        {
            return null;
        }

        try
        {
            return provider switch
            {
                "openai" => await CallOpenAiAsync(systemPrompt, userPrompt, cancellationToken),
                "together" => await CallTogetherAsync(systemPrompt, userPrompt, cancellationToken),
                _ => null
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM planning failed. Falling back to rule-based planner.");
            return null;
        }
    }

    private async Task<ToolPlan?> CallOpenAiAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.OpenAiApiKey))
        {
            return null;
        }

        return await CallOpenAiCompatibleAsync(
            _settings.OpenAiBaseUrl,
            _settings.OpenAiApiKey,
            _settings.OpenAiModel,
            "openai",
            systemPrompt,
            userPrompt,
            cancellationToken);
    }

    private async Task<ToolPlan?> CallTogetherAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.TogetherApiKey))
        {
            return null;
        }

        return await CallOpenAiCompatibleAsync(
            _settings.TogetherBaseUrl,
            _settings.TogetherApiKey,
            _settings.TogetherModel,
            "together",
            systemPrompt,
            userPrompt,
            cancellationToken);
    }

    private async Task<ToolPlan?> CallOpenAiCompatibleAsync(
        string baseUrl,
        string apiKey,
        string model,
        string source,
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            model,
            temperature = 0.1,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            tools = new object[]
            {
                new
                {
                    type = "function",
                    function = new
                    {
                        name = "plan_cad_tools",
                        description = "Build an ordered CAD MCP tool sequence and list assumptions for missing parameters.",
                        parameters = BuildFunctionSchema()
                    }
                }
            },
            tool_choice = "auto"
        };

        var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("{Provider} planning request failed: {StatusCode} {Body}", source, (int)response.StatusCode, content);
            return null;
        }

        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;

        if (!TryGetFirstChoiceMessage(root, out var message))
        {
            return null;
        }

        if (message.TryGetProperty("tool_calls", out var toolCalls) && toolCalls.ValueKind == JsonValueKind.Array)
        {
            foreach (var toolCall in toolCalls.EnumerateArray())
            {
                if (!toolCall.TryGetProperty("function", out var functionNode))
                {
                    continue;
                }

                var functionName = functionNode.GetProperty("name").GetString();
                if (!string.Equals(functionName, "plan_cad_tools", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var argsText = functionNode.GetProperty("arguments").GetString();
                if (string.IsNullOrWhiteSpace(argsText))
                {
                    continue;
                }

                var plan = ParsePlanJson(argsText, source);
                if (plan is not null)
                {
                    return plan;
                }
            }
        }

        if (message.TryGetProperty("content", out var contentNode) && contentNode.ValueKind == JsonValueKind.String)
        {
            var text = contentNode.GetString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                return ParsePlanJson(text, source);
            }
        }

        return null;
    }

    private static object BuildFunctionSchema()
    {
        return new
        {
            type = "object",
            properties = new
            {
                tool_calls = new
                {
                    type = "array",
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            tool_name = new { type = "string" },
                            arguments = new { type = "object" }
                        },
                        required = new[] { "tool_name", "arguments" }
                    }
                },
                assumptions = new
                {
                    type = "array",
                    items = new { type = "string" }
                }
            },
            required = new[] { "tool_calls", "assumptions" }
        };
    }

    private ToolPlan? ParsePlanJson(string json, string source)
    {
        PlanFunctionArgs? planArgs;
        try
        {
            planArgs = JsonSerializer.Deserialize<PlanFunctionArgs>(json, JsonOptions);
        }
        catch
        {
            return null;
        }

        if (planArgs is null || planArgs.ToolCalls.Count == 0)
        {
            return null;
        }

        var plan = new ToolPlan
        {
            Source = source,
            Assumptions = planArgs.Assumptions.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct().ToList()
        };

        foreach (var call in planArgs.ToolCalls)
        {
            if (!AllowedToolNames.Contains(call.ToolName))
            {
                continue;
            }

            var args = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in call.Arguments)
            {
                args[pair.Key] = ConvertJsonElement(pair.Value);
            }

            plan.ToolCalls.Add(new PlannedToolCall
            {
                ToolName = call.ToolName,
                Arguments = args
            });
        }

        return plan.ToolCalls.Count > 0 ? plan : null;
    }

    private static bool TryGetFirstChoiceMessage(JsonElement root, out JsonElement message)
    {
        message = default;
        if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
        {
            return false;
        }

        var firstChoice = choices[0];
        if (!firstChoice.TryGetProperty("message", out var messageNode))
        {
            return false;
        }

        message = messageNode;
        return true;
    }

    private static object? ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt64(out var intValue) => intValue,
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonElement).ToList(),
            JsonValueKind.Object => element.EnumerateObject().ToDictionary(p => p.Name, p => ConvertJsonElement(p.Value)),
            _ => element.GetRawText()
        };
    }

    private sealed class PlanFunctionArgs
    {
        [JsonPropertyName("tool_calls")]
        public List<PlanToolCall> ToolCalls { get; set; } = new();

        [JsonPropertyName("assumptions")]
        public List<string> Assumptions { get; set; } = new();
    }

    private sealed class PlanToolCall
    {
        [JsonPropertyName("tool_name")]
        public string ToolName { get; set; } = string.Empty;

        [JsonPropertyName("arguments")]
        public Dictionary<string, JsonElement> Arguments { get; set; } = new();
    }
}

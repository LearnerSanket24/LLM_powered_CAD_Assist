using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CADMCPServer.Configuration;
using CADMCPServer.Models;
using Microsoft.Extensions.Options;

namespace CADMCPServer.Services.Mcp;

public sealed class HttpMcpClient : IMcpClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly McpSettings _settings;
    private readonly ILogger<HttpMcpClient> _logger;

    public HttpMcpClient(HttpClient httpClient, IOptions<McpSettings> settings, ILogger<HttpMcpClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;

        if (Uri.TryCreate(_settings.BaseUrl, UriKind.Absolute, out var baseUri))
        {
            _httpClient.BaseAddress = baseUri;
        }

        _httpClient.Timeout = TimeSpan.FromSeconds(Math.Max(1, _settings.TimeoutSeconds));
    }

    public async Task<McpToolResponse> ExecuteToolAsync(McpToolRequest request, CancellationToken cancellationToken)
    {
        if (_settings.UseMockResponses)
        {
            return BuildMockResponse(request);
        }

        var payload = new
        {
            tool_name = request.ToolName,
            arguments = request.Arguments
        };

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, _settings.ToolRoute)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };

        var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return new McpToolResponse
            {
                Success = false,
                StatusCode = (int)response.StatusCode,
                Error = new McpError
                {
                    Code = "http_error",
                    Message = $"MCP tool call failed with status {(int)response.StatusCode}.",
                    Details = new JsonObject
                    {
                        ["response_body"] = body
                    },
                    Recoverable = response.StatusCode == System.Net.HttpStatusCode.BadRequest
                }
            };
        }

        try
        {
            var rootNode = JsonNode.Parse(body) as JsonObject ?? new JsonObject();
            var success = rootNode["success"]?.GetValue<bool>() ?? false;
            var result = rootNode["result"] as JsonObject;
            var errorNode = rootNode["error"] as JsonObject;

            var parsedError = errorNode is null
                ? null
                : new McpError
                {
                    Code = errorNode["code"]?.GetValue<string>() ?? "mcp_error",
                    Message = errorNode["message"]?.GetValue<string>() ?? "MCP tool execution failed.",
                    Details = errorNode["details"] as JsonObject,
                    Recoverable = IsLikelyRecoverable(errorNode["code"]?.GetValue<string>(), errorNode["message"]?.GetValue<string>())
                };

            return new McpToolResponse
            {
                Success = success,
                Result = result,
                Error = success ? null : parsedError,
                StatusCode = (int)response.StatusCode
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse MCP response body.");
            return new McpToolResponse
            {
                Success = false,
                StatusCode = (int)response.StatusCode,
                Error = new McpError
                {
                    Code = "invalid_mcp_payload",
                    Message = "MCP response was not valid JSON.",
                    Details = new JsonObject
                    {
                        ["response_body"] = body
                    },
                    Recoverable = false
                }
            };
        }
    }

    private static bool IsLikelyRecoverable(string? code, string? message)
    {
        var text = $"{code} {message}".ToLowerInvariant();
        return text.Contains("eyeshot") || text.Contains("invalid geometry") || text.Contains("constraint") || text.Contains("fillet") || text.Contains("chamfer") || text.Contains("clearance");
    }

    private static McpToolResponse BuildMockResponse(McpToolRequest request)
    {
        var tool = request.ToolName.Trim().ToLowerInvariant();
        var modelId = request.Arguments.TryGetValue("model_id", out var incomingModelId)
            ? incomingModelId?.ToString()
            : null;

        modelId ??= $"mdl-{Guid.NewGuid():N}";

        return tool switch
        {
            "create_gear" => Ok(new JsonObject
            {
                ["model_id"] = modelId,
                ["component"] = "gear",
                ["volume_mm3"] = 8400.5,
                ["mass_kg"] = 0.066,
                ["surface_area_mm2"] = 2190.4
            }),
            "create_shaft" => Ok(new JsonObject
            {
                ["model_id"] = modelId,
                ["component"] = "shaft",
                ["volume_mm3"] = 15300.2,
                ["mass_kg"] = 0.12,
                ["surface_area_mm2"] = 4010.3
            }),
            "create_bearing" => Ok(new JsonObject
            {
                ["model_id"] = modelId,
                ["component"] = "bearing",
                ["volume_mm3"] = 6320.7,
                ["mass_kg"] = 0.049,
                ["surface_area_mm2"] = 1900.9
            }),
            "modify_dim" => Ok(new JsonObject
            {
                ["model_id"] = modelId,
                ["updated"] = true
            }),
            "add_fillet" => Ok(new JsonObject
            {
                ["model_id"] = modelId,
                ["fillet_added"] = true
            }),
            "add_chamfer" => Ok(new JsonObject
            {
                ["model_id"] = modelId,
                ["chamfer_added"] = true
            }),
            "get_volume" => Ok(new JsonObject
            {
                ["model_id"] = modelId,
                ["volume_mm3"] = 11234.8
            }),
            "get_mass" => Ok(new JsonObject
            {
                ["model_id"] = modelId,
                ["mass_kg"] = 0.087
            }),
            "get_surface_area" => Ok(new JsonObject
            {
                ["model_id"] = modelId,
                ["surface_area_mm2"] = 3099.1
            }),
            "measure_clearance" => Ok(new JsonObject
            {
                ["model_id"] = modelId,
                ["clearance_mm"] = 0.12
            }),
            "check_interference" => Ok(new JsonObject
            {
                ["model_id"] = modelId,
                ["interference"] = false
            }),
            "export_step" => Ok(new JsonObject
            {
                ["model_id"] = modelId,
                ["file_path"] = $"exports/{modelId}.step"
            }),
            "export_stl" => Ok(new JsonObject
            {
                ["model_id"] = modelId,
                ["file_path"] = $"exports/{modelId}.stl"
            }),
            "render_viewport" => Ok(new JsonObject
            {
                ["model_id"] = modelId,
                ["image_url"] = $"/model/{modelId}/render"
            }),
            _ => new McpToolResponse
            {
                Success = false,
                StatusCode = 400,
                Error = new McpError
                {
                    Code = "unknown_tool",
                    Message = $"Tool '{request.ToolName}' is not supported.",
                    Details = new JsonObject
                    {
                        ["tool_name"] = request.ToolName
                    },
                    Recoverable = false
                }
            }
        };
    }

    private static McpToolResponse Ok(JsonObject result)
    {
        return new McpToolResponse
        {
            Success = true,
            StatusCode = 200,
            Result = result
        };
    }
}

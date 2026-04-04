using CADMCPServer.Models;
using CADMCPServer.Services.Cad;

namespace CADMCPServer.Services.Mcp;

public sealed class McpToolDispatcher : IMcpToolDispatcher
{
    private readonly ICadEngine _cadEngine;

    public McpToolDispatcher(ICadEngine cadEngine)
    {
        _cadEngine = cadEngine;
    }

    public McpToolResponse Execute(McpToolRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ToolName))
        {
            return new McpToolResponse
            {
                Success = false,
                StatusCode = 400,
                Error = new McpError
                {
                    Code = "validation_error",
                    Message = "tool_name is required.",
                    Recoverable = true
                }
            };
        }

        var args = request.Arguments ?? new Dictionary<string, object?>();
        var response = _cadEngine.Execute(request.ToolName, args);

        if (!response.Success || !request.ToolName.StartsWith("create_", StringComparison.OrdinalIgnoreCase))
        {
            return response;
        }

        var modelId = response.Result?["model_id"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return response;
        }

        var material = args.TryGetValue("material", out var materialObj)
            ? materialObj?.ToString()
            : response.Result?["material"]?.GetValue<string>();

        var volume = _cadEngine.Execute("get_volume", new Dictionary<string, object?>
        {
            ["model_id"] = modelId
        });
        var area = _cadEngine.Execute("get_surface_area", new Dictionary<string, object?>
        {
            ["model_id"] = modelId
        });
        var mass = _cadEngine.Execute("get_mass", new Dictionary<string, object?>
        {
            ["model_id"] = modelId,
            ["material"] = string.IsNullOrWhiteSpace(material) ? "Mild Steel" : material
        });

        if (volume.Success)
        {
            response.Result!["volume_mm3"] = volume.Result?["volume_mm3"]?.GetValue<double>();
        }

        if (area.Success)
        {
            response.Result!["surface_area_mm2"] = area.Result?["surface_area_mm2"]?.GetValue<double>();
        }

        if (mass.Success)
        {
            response.Result!["mass_kg"] = mass.Result?["mass_kg"]?.GetValue<double>();
        }

        return response;
    }

    public object GetSchemas()
    {
        return new
        {
            tools = new object[]
            {
                new
                {
                    name = "create_gear",
                    category = "creation",
                    required = new[] { "teeth", "module", "face_width", "bore_dia" },
                    optional = new[] { "pressure_angle", "material", "session_id" }
                },
                new
                {
                    name = "create_shaft",
                    category = "creation",
                    required = new[] { "length", "diameter", "material" },
                    optional = new[] { "session_id" }
                },
                new
                {
                    name = "create_bearing",
                    category = "creation",
                    required = new[] { "inner_diameter", "outer_diameter", "width" },
                    optional = new[] { "material", "session_id" }
                },
                new
                {
                    name = "modify_dim",
                    category = "modification",
                    required = new[] { "model_id", "param", "value" },
                    optional = new[] { "session_id" }
                },
                new
                {
                    name = "add_fillet",
                    category = "modification",
                    required = new[] { "model_id", "edge_id", "radius" },
                    optional = new[] { "session_id" }
                },
                new
                {
                    name = "add_chamfer",
                    category = "modification",
                    required = new[] { "model_id", "edge_id", "distance" },
                    optional = new[] { "session_id" }
                },
                new
                {
                    name = "get_volume",
                    category = "query",
                    required = new[] { "model_id" },
                    optional = new[] { "session_id" }
                },
                new
                {
                    name = "get_mass",
                    category = "query",
                    required = new[] { "model_id", "material" },
                    optional = new[] { "session_id" }
                },
                new
                {
                    name = "get_surface_area",
                    category = "query",
                    required = new[] { "model_id" },
                    optional = new[] { "session_id" }
                },
                new
                {
                    name = "measure_clearance",
                    category = "assembly",
                    required = new[] { "model_id_a", "model_id_b" },
                    optional = new[] { "center_distance_mm", "session_id" }
                },
                new
                {
                    name = "check_interference",
                    category = "assembly",
                    required = new[] { "model_id_a", "model_id_b" },
                    optional = new[] { "center_distance_mm", "session_id" }
                },
                new
                {
                    name = "export_step",
                    category = "export",
                    required = new[] { "model_id", "file_path" },
                    optional = new[] { "session_id" }
                },
                new
                {
                    name = "export_stl",
                    category = "export",
                    required = new[] { "model_id", "file_path" },
                    optional = new[] { "resolution", "session_id" }
                },
                new
                {
                    name = "render_viewport",
                    category = "export",
                    required = new[] { "model_id" },
                    optional = new[] { "angle", "session_id" }
                }
            }
        };
    }
}

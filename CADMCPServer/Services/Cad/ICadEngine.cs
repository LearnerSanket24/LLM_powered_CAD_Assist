using System.Text.Json.Nodes;
using CADMCPServer.Models;

namespace CADMCPServer.Services.Cad;

public interface ICadEngine
{
    McpToolResponse Execute(string toolName, Dictionary<string, object?> arguments);
    bool TryGetModel(string modelId, out CadModelState? model);
}

public sealed class CadModelState
{
    public string ModelId { get; set; } = string.Empty;
    public string ComponentType { get; set; } = string.Empty;
    public string Material { get; set; } = "Mild Steel";
    public Dictionary<string, double> Parameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public double VolumeMm3 { get; set; }
    public double SurfaceAreaMm2 { get; set; }
    public double MassKg { get; set; }
    public double EnvelopeDiameterMm { get; set; }
    public double EnvelopeLengthMm { get; set; }
    public List<string> FeatureLog { get; set; } = new();

    public JsonObject ToJson()
    {
        var parametersNode = new JsonObject();
        foreach (var pair in Parameters)
        {
            parametersNode[pair.Key] = pair.Value;
        }

        return new JsonObject
        {
            ["model_id"] = ModelId,
            ["component_type"] = ComponentType,
            ["material"] = Material,
            ["volume_mm3"] = VolumeMm3,
            ["surface_area_mm2"] = SurfaceAreaMm2,
            ["mass_kg"] = MassKg,
            ["envelope_diameter_mm"] = EnvelopeDiameterMm,
            ["envelope_length_mm"] = EnvelopeLengthMm,
            ["parameters"] = parametersNode,
            ["features"] = new JsonArray(FeatureLog.Select(v => (JsonNode?)v).ToArray())
        };
    }
}

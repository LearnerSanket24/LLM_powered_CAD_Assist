using System.Text.Json;
using System.Text.Json.Serialization;

namespace CADMCPServer.Models;

public sealed class AnalyzeRequest
{
    public string SessionId { get; set; } = "default";
    public string UserInput { get; set; } = string.Empty;
    public string? ModelId { get; set; }
    public Dictionary<string, JsonElement>? Overrides { get; set; }

    [JsonPropertyName("component_type")]
    public string? ComponentType { get; set; }

    [JsonPropertyName("material")]
    public string? Material { get; set; }

    [JsonPropertyName("geometry_input")]
    public GeometryInput GeometryInput { get; set; } = new();

    [JsonPropertyName("load_input")]
    public LoadInput LoadInput { get; set; } = new();
}

public sealed class GeometryInput
{
    [JsonPropertyName("module_mm")]
    public double? ModuleMm { get; set; }

    [JsonPropertyName("teeth_count")]
    public int? TeethCount { get; set; }

    [JsonPropertyName("face_width_mm")]
    public double? FaceWidthMm { get; set; }

    [JsonPropertyName("shaft_diameter_mm")]
    public double? ShaftDiameterMm { get; set; }

    [JsonPropertyName("shaft_length_mm")]
    public double? ShaftLengthMm { get; set; }

    [JsonPropertyName("bearing_inner_diameter_mm")]
    public double? BearingInnerDiameterMm { get; set; }

    [JsonPropertyName("bearing_outer_diameter_mm")]
    public double? BearingOuterDiameterMm { get; set; }

    [JsonPropertyName("bearing_width_mm")]
    public double? BearingWidthMm { get; set; }

    [JsonPropertyName("wall_thickness_mm")]
    public double? WallThicknessMm { get; set; }

    [JsonPropertyName("draft_angle_deg")]
    public double? DraftAngleDeg { get; set; }

    [JsonPropertyName("has_undercut")]
    public bool? HasUndercut { get; set; }

    [JsonPropertyName("thread_pitch_mm")]
    public double? ThreadPitchMm { get; set; }

    [JsonPropertyName("projected_area_mm2")]
    public double? ProjectedAreaMm2 { get; set; }
}

public sealed class LoadInput
{
    [JsonPropertyName("tangential_force_n")]
    public double? TangentialForceN { get; set; }

    [JsonPropertyName("radial_force_n")]
    public double? RadialForceN { get; set; }

    [JsonPropertyName("axial_force_n")]
    public double? AxialForceN { get; set; }

    [JsonPropertyName("torque_nmm")]
    public double? TorqueNmm { get; set; }

    [JsonPropertyName("applied_load_n")]
    public double? AppliedLoadN { get; set; }
}

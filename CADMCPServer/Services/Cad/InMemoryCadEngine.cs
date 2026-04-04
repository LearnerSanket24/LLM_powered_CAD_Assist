using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using CADMCPServer.Configuration;
using CADMCPServer.Models;
using Microsoft.Extensions.Options;

namespace CADMCPServer.Services.Cad;

public sealed class InMemoryCadEngine : ICadEngine
{
    private static readonly IReadOnlyDictionary<string, double> DensityByMaterialKgPerMm3 = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
    {
        ["Mild Steel"] = 7.85e-6,
        ["Medium Carbon Steel"] = 7.85e-6,
        ["Carbon Steel"] = 7.85e-6,
        ["Alloy Steel"] = 7.80e-6,
        ["Aluminium 6061-T6"] = 2.70e-6,
        ["Nylon PA66"] = 1.14e-6
    };

    private readonly ConcurrentDictionary<string, CadModelState> _models = new(StringComparer.OrdinalIgnoreCase);
    private readonly OutputSettings _outputSettings;

    public InMemoryCadEngine(IOptions<OutputSettings> outputSettings)
    {
        _outputSettings = outputSettings.Value;
    }

    public bool TryGetModel(string modelId, out CadModelState? model)
    {
        if (_models.TryGetValue(modelId, out var existing))
        {
            model = existing;
            return true;
        }

        model = null;
        return false;
    }

    public McpToolResponse Execute(string toolName, Dictionary<string, object?> arguments)
    {
        try
        {
            return toolName.Trim().ToLowerInvariant() switch
            {
                "create_gear" => CreateGear(arguments),
                "create_shaft" => CreateShaft(arguments),
                "create_bearing" => CreateBearing(arguments),
                "modify_dim" => ModifyDim(arguments),
                "add_fillet" => AddFeature(arguments, "fillet"),
                "add_chamfer" => AddFeature(arguments, "chamfer"),
                "get_volume" => QueryMetric(arguments, "volume_mm3"),
                "get_mass" => QueryMass(arguments),
                "get_surface_area" => QueryMetric(arguments, "surface_area_mm2"),
                "measure_clearance" => MeasureClearance(arguments),
                "check_interference" => CheckInterference(arguments),
                "export_step" => Export(arguments, "step"),
                "export_stl" => Export(arguments, "stl"),
                "render_viewport" => RenderViewport(arguments),
                _ => Fail("unknown_tool", $"Tool '{toolName}' is not supported.", false, new JsonObject { ["tool_name"] = toolName }, 400)
            };
        }
        catch (CadEngineException ex)
        {
            return Fail(ex.Code, ex.Message, ex.Recoverable, ex.Details, ex.StatusCode);
        }
        catch (Exception ex)
        {
            return Fail(
                "cad_engine_unhandled",
                "Unhandled CAD engine error.",
                false,
                new JsonObject { ["exception"] = ex.Message },
                500);
        }
    }

    private McpToolResponse CreateGear(Dictionary<string, object?> args)
    {
        var teeth = GetInt(args, "teeth", 12, 300);
        var module = GetDouble(args, "module", 0.25, 20);
        var faceWidth = GetDouble(args, "face_width", 1, 500);
        var pressureAngle = GetDouble(args, "pressure_angle", 10, 35, defaultValue: 20);

        var pitchDiameter = module * teeth;
        var outerDiameter = module * (teeth + 2);
        var rootDiameter = Math.Max(module * (teeth - 2.5), module * 5);
        var boreDia = GetDouble(args, "bore_dia", 0.1, outerDiameter * 0.9, defaultValue: Math.Max(1, pitchDiameter * 0.2));

        if (boreDia >= rootDiameter)
        {
            throw new CadEngineException(
                "invalid_geometry",
                "Bore diameter is too large for the selected gear profile.",
                true,
                new JsonObject
                {
                    ["bore_dia"] = boreDia,
                    ["max_allowed_bore_dia"] = rootDiameter - 0.1
                },
                400);
        }

        // Approximate involute spur gear body as annular cylinder with tooth fill factor.
        var annulusArea = Math.PI * 0.25 * (Math.Pow(outerDiameter, 2) - Math.Pow(boreDia, 2));
        var fillFactor = 0.68 + Math.Min(0.1, (pressureAngle - 20) * 0.01);
        var volume = annulusArea * faceWidth * fillFactor;
        var sideArea = Math.PI * outerDiameter * faceWidth;
        var faceArea = 2 * annulusArea;
        var surfaceArea = sideArea + faceArea;

        var material = GetString(args, "material", "Mild Steel")!;
        var mass = ComputeMass(material, volume);

        var model = new CadModelState
        {
            ModelId = NewModelId("gear"),
            ComponentType = "gear",
            Material = material,
            VolumeMm3 = Round4(volume),
            SurfaceAreaMm2 = Round4(surfaceArea),
            MassKg = Round6(mass),
            EnvelopeDiameterMm = Round4(outerDiameter),
            EnvelopeLengthMm = Round4(faceWidth),
            Parameters = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["teeth"] = teeth,
                ["module"] = module,
                ["face_width"] = faceWidth,
                ["pressure_angle"] = pressureAngle,
                ["bore_dia"] = boreDia,
                ["pitch_diameter"] = pitchDiameter,
                ["outer_diameter"] = outerDiameter
            }
        };

        _models[model.ModelId] = model;
        return Ok(model.ToJson());
    }

    private McpToolResponse CreateShaft(Dictionary<string, object?> args)
    {
        var length = GetDouble(args, "length", 1, 4000);
        var diameter = GetDouble(args, "diameter", 0.5, 1000);
        var material = GetString(args, "material", "Mild Steel")!;

        var radius = diameter * 0.5;
        var volume = Math.PI * radius * radius * length;
        var surfaceArea = (2 * Math.PI * radius * length) + (2 * Math.PI * radius * radius);
        var mass = ComputeMass(material, volume);

        var model = new CadModelState
        {
            ModelId = NewModelId("shaft"),
            ComponentType = "shaft",
            Material = material,
            VolumeMm3 = Round4(volume),
            SurfaceAreaMm2 = Round4(surfaceArea),
            MassKg = Round6(mass),
            EnvelopeDiameterMm = Round4(diameter),
            EnvelopeLengthMm = Round4(length),
            Parameters = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["length"] = length,
                ["diameter"] = diameter
            }
        };

        _models[model.ModelId] = model;
        return Ok(model.ToJson());
    }

    private McpToolResponse CreateBearing(Dictionary<string, object?> args)
    {
        var inner = GetDoubleFromKeys(args, new[] { "inner_diameter", "inner_dia" }, 1, 2000);
        var outer = GetDoubleFromKeys(args, new[] { "outer_diameter", "outer_dia" }, inner + 0.5, 4000);
        var width = GetDouble(args, "width", 0.5, 1000);
        var material = GetString(args, "material", "Mild Steel")!;

        if (outer <= inner)
        {
            throw new CadEngineException(
                "invalid_geometry",
                "outer_diameter must be greater than inner_diameter.",
                true,
                new JsonObject { ["inner_diameter"] = inner, ["outer_diameter"] = outer },
                400);
        }

        var area = Math.PI * 0.25 * (Math.Pow(outer, 2) - Math.Pow(inner, 2));
        var volume = area * width;
        var sideArea = Math.PI * (outer + inner) * width;
        var surfaceArea = sideArea + (2 * area);
        var mass = ComputeMass(material, volume);

        var model = new CadModelState
        {
            ModelId = NewModelId("bearing"),
            ComponentType = "bearing",
            Material = material,
            VolumeMm3 = Round4(volume),
            SurfaceAreaMm2 = Round4(surfaceArea),
            MassKg = Round6(mass),
            EnvelopeDiameterMm = Round4(outer),
            EnvelopeLengthMm = Round4(width),
            Parameters = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["inner_diameter"] = inner,
                ["outer_diameter"] = outer,
                ["width"] = width
            }
        };

        _models[model.ModelId] = model;
        return Ok(model.ToJson());
    }

    private McpToolResponse ModifyDim(Dictionary<string, object?> args)
    {
        var model = GetModelFromArgs(args);
        var param = GetString(args, "param", null) ?? GetString(args, "dimension", null);
        if (string.IsNullOrWhiteSpace(param))
        {
            throw new CadEngineException("validation_error", "param is required.", true, null, 400);
        }

        var value = GetDouble(args, "value", 0.0001, 100000);
        model.Parameters[param] = value;

        Recompute(model);

        var result = model.ToJson();
        result["updated_param"] = param;
        result["updated_value"] = value;
        return Ok(result);
    }

    private McpToolResponse AddFeature(Dictionary<string, object?> args, string featureType)
    {
        var model = GetModelFromArgs(args);
        var edgeId = GetString(args, "edge_id", "E0")!;
        var key = featureType == "fillet" ? "radius" : "distance";
        var magnitude = GetDouble(args, key, 0.01, 100);

        model.FeatureLog.Add($"{featureType}:{edgeId}:{magnitude.ToString(CultureInfo.InvariantCulture)}");

        // Simple approximation: feature slightly reduces volume and increases surface area.
        model.VolumeMm3 = Round4(model.VolumeMm3 * 0.998);
        model.SurfaceAreaMm2 = Round4(model.SurfaceAreaMm2 * 1.002);
        model.MassKg = Round6(ComputeMass(model.Material, model.VolumeMm3));

        var result = model.ToJson();
        result[$"{featureType}_added"] = true;
        result["edge_id"] = edgeId;
        result[key] = magnitude;
        return Ok(result);
    }

    private McpToolResponse QueryMetric(Dictionary<string, object?> args, string metric)
    {
        var model = GetModelFromArgs(args);
        var result = new JsonObject { ["model_id"] = model.ModelId };

        switch (metric)
        {
            case "volume_mm3":
                result[metric] = model.VolumeMm3;
                break;
            case "surface_area_mm2":
                result[metric] = model.SurfaceAreaMm2;
                break;
            default:
                throw new CadEngineException("unsupported_metric", $"Metric '{metric}' is not supported.", false, null, 400);
        }

        return Ok(result);
    }

    private McpToolResponse QueryMass(Dictionary<string, object?> args)
    {
        var model = GetModelFromArgs(args);
        var material = GetString(args, "material", model.Material)!;
        var mass = ComputeMass(material, model.VolumeMm3);

        var result = new JsonObject
        {
            ["model_id"] = model.ModelId,
            ["material"] = material,
            ["mass_kg"] = Round6(mass)
        };

        return Ok(result);
    }

    private McpToolResponse MeasureClearance(Dictionary<string, object?> args)
    {
        var modelA = GetModel(args, "model_id_a");
        var modelB = GetModel(args, "model_id_b");

        // Center distance assumption for prototype assembly check.
        var centerDistance = args.TryGetValue("center_distance_mm", out _)
            ? GetDouble(args, "center_distance_mm", 0.01, 100000)
            : Math.Max(modelA.EnvelopeDiameterMm, modelB.EnvelopeDiameterMm) * 0.55;
        var required = (modelA.EnvelopeDiameterMm + modelB.EnvelopeDiameterMm) * 0.5;
        var clearance = Round4(centerDistance - required);

        var result = new JsonObject
        {
            ["model_id_a"] = modelA.ModelId,
            ["model_id_b"] = modelB.ModelId,
            ["center_distance_mm"] = Round4(centerDistance),
            ["clearance_mm"] = clearance
        };

        return Ok(result);
    }

    private McpToolResponse CheckInterference(Dictionary<string, object?> args)
    {
        var clearanceResponse = MeasureClearance(args);
        var clearance = clearanceResponse.Result?["clearance_mm"]?.GetValue<double>() ?? 0;
        var overlapDepth = Math.Max(0, -clearance);
        var modelA = GetModel(args, "model_id_a");
        var modelB = GetModel(args, "model_id_b");
        var overlapArea = Math.PI * Math.Pow(Math.Min(modelA.EnvelopeDiameterMm, modelB.EnvelopeDiameterMm) * 0.25, 2);
        var overlapVolume = Round4(overlapDepth * overlapArea);

        var result = new JsonObject
        {
            ["model_id_a"] = clearanceResponse.Result?["model_id_a"]?.GetValue<string>(),
            ["model_id_b"] = clearanceResponse.Result?["model_id_b"]?.GetValue<string>(),
            ["interference"] = clearance < 0,
            ["clearance_mm"] = clearance,
            ["volume_mm3"] = overlapVolume
        };

        return Ok(result);
    }

    private McpToolResponse Export(Dictionary<string, object?> args, string extension)
    {
        var model = GetModelFromArgs(args);
        var fullPath = BuildOutputPath(args, model.ModelId, extension);

        if (extension.Equals("step", StringComparison.OrdinalIgnoreCase))
        {
            WriteStepFile(fullPath, model);
        }
        else
        {
            WriteAsciiStl(fullPath, model);
        }

        return Ok(new JsonObject
        {
            ["model_id"] = model.ModelId,
            ["file_path"] = fullPath,
            ["format"] = extension,
            ["status"] = "exported"
        });
    }

    private McpToolResponse RenderViewport(Dictionary<string, object?> args)
    {
        var model = GetModelFromArgs(args);
        var angle = GetString(args, "angle", "isometric")!;
        var fullPath = BuildOutputPath(args, model.ModelId, "svg", suffix: "-viewport");

        var radius = Math.Max(12, model.EnvelopeDiameterMm * 0.15);
        var length = Math.Max(18, model.EnvelopeLengthMm * 0.12);

        var svg = $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"720\" height=\"420\">"
            + "<rect width=\"100%\" height=\"100%\" fill=\"#0f1b2b\"/>"
            + $"<circle cx=\"220\" cy=\"210\" r=\"{radius.ToString(CultureInfo.InvariantCulture)}\" fill=\"#2ca9e1\" opacity=\"0.9\"/>"
            + $"<rect x=\"360\" y=\"{(210 - (length * 0.5)).ToString(CultureInfo.InvariantCulture)}\" width=\"120\" height=\"{length.ToString(CultureInfo.InvariantCulture)}\" rx=\"8\" fill=\"#7fd8ff\" opacity=\"0.8\"/>"
            + $"<text x=\"40\" y=\"42\" fill=\"#d7f2ff\" font-family=\"Segoe UI\" font-size=\"22\">Model {model.ModelId}</text>"
            + $"<text x=\"40\" y=\"74\" fill=\"#a5dfff\" font-family=\"Segoe UI\" font-size=\"16\">Component: {model.ComponentType} | Angle: {angle}</text>"
            + "</svg>";

        File.WriteAllText(fullPath, svg, Encoding.UTF8);

        return Ok(new JsonObject
        {
            ["model_id"] = model.ModelId,
            ["angle"] = angle,
            ["image_path"] = fullPath
        });
    }

    private string BuildOutputPath(Dictionary<string, object?> args, string modelId, string extension, string suffix = "")
    {
        var configuredRoot = string.IsNullOrWhiteSpace(_outputSettings.RootDirectory)
            ? "outputs"
            : _outputSettings.RootDirectory;
        var baseRoot = Path.Combine(AppContext.BaseDirectory, configuredRoot.Replace('/', Path.DirectorySeparatorChar));

        var sessionId = GetString(args, "session_id", "default") ?? "default";
        sessionId = SanitizePathSegment(sessionId);

        var root = _outputSettings.SessionIsolationEnabled
            ? Path.Combine(baseRoot, sessionId)
            : baseRoot;

        Directory.CreateDirectory(root);

        var requested = GetString(args, "file_path", string.Empty);
        var requestedName = string.IsNullOrWhiteSpace(requested)
            ? $"{modelId}{suffix}.{extension}"
            : Path.GetFileName(requested.Replace('/', Path.DirectorySeparatorChar));

        var cleanName = string.IsNullOrWhiteSpace(requestedName)
            ? $"{modelId}{suffix}.{extension}"
            : requestedName;

        if (!cleanName.EndsWith($".{extension}", StringComparison.OrdinalIgnoreCase))
        {
            cleanName = Path.GetFileNameWithoutExtension(cleanName) + $".{extension}";
        }

        return Path.Combine(root, cleanName);
    }

    private static string SanitizePathSegment(string input)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = input.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        var value = new string(chars);
        return string.IsNullOrWhiteSpace(value) ? "default" : value;
    }

    private static void WriteStepFile(string path, CadModelState model)
    {
        var sb = new StringBuilder();
        sb.AppendLine("ISO-10303-21;");
        sb.AppendLine("HEADER;");
        sb.AppendLine("FILE_DESCRIPTION(('CADMCPServer STEP export'),'2;1');");
        sb.AppendLine($"FILE_NAME('{Path.GetFileName(path)}','{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss}',('CADMCPServer'),('CADMCPServer'),'','CADMCPServer','');");
        sb.AppendLine("FILE_SCHEMA(('CONFIG_CONTROL_DESIGN')); ");
        sb.AppendLine("ENDSEC;");
        sb.AppendLine("DATA;");
        sb.AppendLine("#10=CARTESIAN_POINT('',(0.,0.,0.));");
        sb.AppendLine($"#11=CARTESIAN_POINT('',({model.EnvelopeDiameterMm.ToString(CultureInfo.InvariantCulture)},0.,0.));");
        sb.AppendLine($"#12=CARTESIAN_POINT('',(0.,{model.EnvelopeLengthMm.ToString(CultureInfo.InvariantCulture)},0.));");
        sb.AppendLine("ENDSEC;");
        sb.AppendLine("END-ISO-10303-21;");

        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    private static void WriteAsciiStl(string path, CadModelState model)
    {
        var halfD = Math.Max(0.5, model.EnvelopeDiameterMm * 0.5);
        var halfL = Math.Max(0.5, model.EnvelopeLengthMm * 0.5);

        var p = new[]
        {
            (-halfD, -halfD, -halfL),
            (halfD, -halfD, -halfL),
            (halfD, halfD, -halfL),
            (-halfD, halfD, -halfL),
            (-halfD, -halfD, halfL),
            (halfD, -halfD, halfL),
            (halfD, halfD, halfL),
            (-halfD, halfD, halfL)
        };

        var faces = new[]
        {
            (0, 1, 2), (0, 2, 3),
            (4, 6, 5), (4, 7, 6),
            (0, 4, 5), (0, 5, 1),
            (1, 5, 6), (1, 6, 2),
            (2, 6, 7), (2, 7, 3),
            (3, 7, 4), (3, 4, 0)
        };

        var sb = new StringBuilder();
        sb.AppendLine($"solid {model.ModelId}");
        foreach (var (a, b, c) in faces)
        {
            sb.AppendLine("  facet normal 0 0 0");
            sb.AppendLine("    outer loop");
            WriteVertex(sb, p[a]);
            WriteVertex(sb, p[b]);
            WriteVertex(sb, p[c]);
            sb.AppendLine("    endloop");
            sb.AppendLine("  endfacet");
        }
        sb.AppendLine($"endsolid {model.ModelId}");

        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    private static void WriteVertex(StringBuilder sb, (double x, double y, double z) v)
    {
        sb.AppendLine(
            $"      vertex {v.x.ToString(CultureInfo.InvariantCulture)} {v.y.ToString(CultureInfo.InvariantCulture)} {v.z.ToString(CultureInfo.InvariantCulture)}");
    }

    private static double GetDoubleFromKeys(Dictionary<string, object?> args, IEnumerable<string> keys, double min, double max)
    {
        foreach (var key in keys)
        {
            if (args.ContainsKey(key))
            {
                return GetDouble(args, key, min, max);
            }
        }

        throw new CadEngineException("validation_error", $"One of [{string.Join(", ", keys)}] is required.", true, null, 400);
    }

    private CadModelState GetModelFromArgs(Dictionary<string, object?> args)
    {
        return GetModel(args, "model_id");
    }

    private CadModelState GetModel(Dictionary<string, object?> args, string key)
    {
        var modelId = GetString(args, key, null);
        if (string.IsNullOrWhiteSpace(modelId))
        {
            throw new CadEngineException("validation_error", $"{key} is required.", true, null, 400);
        }

        if (_models.TryGetValue(modelId, out var model))
        {
            return model;
        }

        throw new CadEngineException(
            "model_not_found",
            $"Model '{modelId}' was not found.",
            false,
            new JsonObject { ["model_id"] = modelId },
            404);
    }

    private static string NewModelId(string prefix)
    {
        return $"{prefix}-{Guid.NewGuid():N}";
    }

    private static double GetDouble(Dictionary<string, object?> args, string key, double min, double max, double? defaultValue = null)
    {
        if (!args.TryGetValue(key, out var raw) || raw is null)
        {
            if (defaultValue.HasValue)
            {
                return defaultValue.Value;
            }

            throw new CadEngineException("validation_error", $"{key} is required.", true, null, 400);
        }

        if (!double.TryParse(raw.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
        {
            throw new CadEngineException(
                "validation_error",
                $"{key} must be numeric.",
                true,
                new JsonObject { ["input"] = raw?.ToString() },
                400);
        }

        if (value < min || value > max)
        {
            throw new CadEngineException(
                "invalid_geometry",
                $"{key} is outside valid range [{min}, {max}].",
                true,
                new JsonObject { ["value"] = value, ["min"] = min, ["max"] = max },
                400);
        }

        return value;
    }

    private static int GetInt(Dictionary<string, object?> args, string key, int min, int max)
    {
        if (!args.TryGetValue(key, out var raw) || raw is null)
        {
            throw new CadEngineException("validation_error", $"{key} is required.", true, null, 400);
        }

        if (!int.TryParse(raw.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            throw new CadEngineException("validation_error", $"{key} must be an integer.", true, null, 400);
        }

        if (value < min || value > max)
        {
            throw new CadEngineException(
                "invalid_geometry",
                $"{key} is outside valid range [{min}, {max}].",
                true,
                new JsonObject { ["value"] = value, ["min"] = min, ["max"] = max },
                400);
        }

        return value;
    }

    private static string? GetString(Dictionary<string, object?> args, string key, string? defaultValue)
    {
        if (!args.TryGetValue(key, out var raw) || raw is null)
        {
            return defaultValue;
        }

        var value = raw.ToString();
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value.Trim();
    }

    private static double ComputeMass(string material, double volumeMm3)
    {
        var density = DensityByMaterialKgPerMm3.TryGetValue(material, out var known)
            ? known
            : DensityByMaterialKgPerMm3["Mild Steel"];

        return volumeMm3 * density;
    }

    private static double Round4(double value) => Math.Round(value, 4);
    private static double Round6(double value) => Math.Round(value, 6);

    private static McpToolResponse Ok(JsonObject result)
    {
        return new McpToolResponse
        {
            Success = true,
            Result = result,
            StatusCode = 200
        };
    }

    private static McpToolResponse Fail(string code, string message, bool recoverable, JsonObject? details, int statusCode)
    {
        return new McpToolResponse
        {
            Success = false,
            StatusCode = statusCode,
            Error = new McpError
            {
                Code = code,
                Message = message,
                Details = details,
                Recoverable = recoverable
            }
        };
    }

    private static void Recompute(CadModelState model)
    {
        if (model.ComponentType.Equals("gear", StringComparison.OrdinalIgnoreCase))
        {
            var teeth = model.Parameters.TryGetValue("teeth", out var t) ? Math.Max(8, t) : 20;
            var module = model.Parameters.TryGetValue("module", out var m) ? Math.Max(0.25, m) : 2;
            var faceWidth = model.Parameters.TryGetValue("face_width", out var fw) ? Math.Max(1, fw) : 20;
            var bore = model.Parameters.TryGetValue("bore_dia", out var bd) ? Math.Max(0.1, bd) : Math.Max(1, module * teeth * 0.2);
            var pressure = model.Parameters.TryGetValue("pressure_angle", out var pa) ? pa : 20;

            var outerDiameter = module * (teeth + 2);
            var annulusArea = Math.PI * 0.25 * (Math.Pow(outerDiameter, 2) - Math.Pow(bore, 2));
            var fillFactor = 0.68 + Math.Min(0.1, (pressure - 20) * 0.01);
            var volume = annulusArea * faceWidth * fillFactor;
            var surfaceArea = (Math.PI * outerDiameter * faceWidth) + (2 * annulusArea);

            model.VolumeMm3 = Round4(volume);
            model.SurfaceAreaMm2 = Round4(surfaceArea);
            model.EnvelopeDiameterMm = Round4(outerDiameter);
            model.EnvelopeLengthMm = Round4(faceWidth);
            model.MassKg = Round6(ComputeMass(model.Material, volume));
            return;
        }

        if (model.ComponentType.Equals("shaft", StringComparison.OrdinalIgnoreCase))
        {
            var length = model.Parameters.TryGetValue("length", out var l) ? Math.Max(1, l) : 100;
            var diameter = model.Parameters.TryGetValue("diameter", out var d) ? Math.Max(0.5, d) : 20;
            var radius = diameter * 0.5;
            var volume = Math.PI * radius * radius * length;
            var surfaceArea = (2 * Math.PI * radius * length) + (2 * Math.PI * radius * radius);

            model.VolumeMm3 = Round4(volume);
            model.SurfaceAreaMm2 = Round4(surfaceArea);
            model.EnvelopeDiameterMm = Round4(diameter);
            model.EnvelopeLengthMm = Round4(length);
            model.MassKg = Round6(ComputeMass(model.Material, volume));
            return;
        }

        if (model.ComponentType.Equals("bearing", StringComparison.OrdinalIgnoreCase))
        {
            var inner = model.Parameters.TryGetValue("inner_diameter", out var i) ? Math.Max(1, i) : 20;
            var outer = model.Parameters.TryGetValue("outer_diameter", out var o) ? Math.Max(inner + 0.5, o) : 47;
            var width = model.Parameters.TryGetValue("width", out var w) ? Math.Max(0.5, w) : 14;

            var area = Math.PI * 0.25 * (Math.Pow(outer, 2) - Math.Pow(inner, 2));
            var volume = area * width;
            var sideArea = Math.PI * (outer + inner) * width;
            var surfaceArea = sideArea + (2 * area);

            model.VolumeMm3 = Round4(volume);
            model.SurfaceAreaMm2 = Round4(surfaceArea);
            model.EnvelopeDiameterMm = Round4(outer);
            model.EnvelopeLengthMm = Round4(width);
            model.MassKg = Round6(ComputeMass(model.Material, volume));
        }
    }

    private sealed class CadEngineException : Exception
    {
        public CadEngineException(string code, string message, bool recoverable, JsonObject? details, int statusCode)
            : base(message)
        {
            Code = code;
            Recoverable = recoverable;
            Details = details;
            StatusCode = statusCode;
        }

        public string Code { get; }
        public bool Recoverable { get; }
        public JsonObject? Details { get; }
        public int StatusCode { get; }
    }
}

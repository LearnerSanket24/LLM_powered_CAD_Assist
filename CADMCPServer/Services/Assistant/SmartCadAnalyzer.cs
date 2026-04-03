using CADMCPServer.Models;

namespace CADMCPServer.Services.Assistant;

public sealed class SmartCadAnalyzer : ISmartCadAnalyzer
{
    private static readonly IReadOnlyDictionary<int, double> LewisFormFactorByTeeth = new Dictionary<int, double>
    {
        [12] = 0.245,
        [16] = 0.289,
        [20] = 0.322,
        [24] = 0.343,
        [30] = 0.365,
        [40] = 0.392,
        [60] = 0.421,
        [80] = 0.435,
        [120] = 0.452
    };

    private static readonly IReadOnlyDictionary<string, double> YieldStrengthByMaterialMpa = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
    {
        ["Mild Steel"] = 250,
        ["Medium Carbon Steel"] = 370,
        ["20CrMnTi"] = 620,
        ["Aluminium 6061"] = 276,
        ["Nylon PA66"] = 80
    };

    public SmartCadAnalysis Analyze(AnalyzeRequest request, ConversationContext context, IReadOnlyCollection<string> orchestrationAssumptions)
    {
        var assumptions = new List<string>();
        var recommendations = new List<string>();
        var metrics = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        var material = ResolveMaterial(request.Material, assumptions);
        var yieldStrengthMpa = YieldStrengthByMaterialMpa[material];
        metrics["yield_strength_mpa"] = yieldStrengthMpa;

        var componentType = ResolveComponentType(request.ComponentType, context.LastComponentType, assumptions);
        var geometry = request.GeometryInput ?? new GeometryInput();
        var load = request.LoadInput ?? new LoadInput();

        var status = "PASS";

        switch (componentType)
        {
            case "gear":
            {
                var faceWidthMm = GetValue(geometry.FaceWidthMm, 20, assumptions, "face_width_mm");
                var moduleMm = GetValue(geometry.ModuleMm, 2, assumptions, "module_mm");
                var teethCount = (int)GetValue(geometry.TeethCount, 20, assumptions, "teeth_count");
                var tangentialForceN = GetValue(load.TangentialForceN, 1000, assumptions, "tangential_force_n");
                var yFactor = ResolveLewisFactor(teethCount);

                var denominator = Math.Max(0.001, faceWidthMm * moduleMm * yFactor);
                var bendingStressMpa = tangentialForceN / denominator;
                var safetyFactor = yieldStrengthMpa / Math.Max(0.001, bendingStressMpa);

                metrics["lewis_form_factor_y"] = yFactor;
                metrics["bending_stress_mpa"] = bendingStressMpa;
                metrics["safety_factor"] = safetyFactor;

                status = ApplySafetyFactorStatus(status, safetyFactor, recommendations);
                break;
            }
            case "shaft":
            {
                var diameterMm = GetValue(geometry.ShaftDiameterMm, 20, assumptions, "shaft_diameter_mm");
                var lengthMm = GetValue(geometry.ShaftLengthMm, 100, assumptions, "shaft_length_mm");
                var torqueNmm = GetValue(load.TorqueNmm, 50000, assumptions, "torque_nmm");

                var shearMpa = (16 * torqueNmm) / (Math.PI * Math.Pow(Math.Max(0.1, diameterMm), 3));
                var vonMisesMpa = shearMpa * Math.Sqrt(3);
                var slenderness = lengthMm / Math.Max(0.1, diameterMm);
                var safetyFactor = yieldStrengthMpa / Math.Max(0.001, vonMisesMpa);

                metrics["torsional_shear_stress_mpa"] = shearMpa;
                metrics["equivalent_stress_mpa"] = vonMisesMpa;
                metrics["slenderness_ratio"] = slenderness;
                metrics["safety_factor"] = safetyFactor;

                status = ApplySafetyFactorStatus(status, safetyFactor, recommendations);
                if (slenderness > 20)
                {
                    status = Elevate(status, "WARNING");
                    recommendations.Add("Reduce shaft length or increase diameter to avoid deflection risk.");
                }

                break;
            }
            case "bearing":
            {
                var innerDiameterMm = GetValue(geometry.BearingInnerDiameterMm, 20, assumptions, "bearing_inner_diameter_mm");
                var outerDiameterMm = GetValue(geometry.BearingOuterDiameterMm, 47, assumptions, "bearing_outer_diameter_mm");
                var widthMm = GetValue(geometry.BearingWidthMm, 14, assumptions, "bearing_width_mm");
                var appliedLoadN = GetValue(load.AppliedLoadN ?? load.RadialForceN, 2000, assumptions, "applied_load_n");

                var projectedArea = Math.Max(1, innerDiameterMm * widthMm);
                var contactPressureMpa = appliedLoadN / projectedArea;
                var sizeRatio = outerDiameterMm / Math.Max(0.1, innerDiameterMm);
                var safetyFactor = yieldStrengthMpa / Math.Max(0.001, contactPressureMpa);

                metrics["contact_pressure_mpa"] = contactPressureMpa;
                metrics["outer_to_inner_ratio"] = sizeRatio;
                metrics["safety_factor"] = safetyFactor;

                status = ApplySafetyFactorStatus(status, safetyFactor, recommendations);
                if (sizeRatio < 1.5)
                {
                    status = Elevate(status, "WARNING");
                    recommendations.Add("Increase outer diameter or reduce inner diameter for stronger bearing section.");
                }

                break;
            }
            default:
            {
                var projectedAreaMm2 = GetValue(geometry.ProjectedAreaMm2, 500, assumptions, "projected_area_mm2");
                var totalLoadN = GetTotalLoad(load, assumptions);
                var projectedStressMpa = totalLoadN / Math.Max(1, projectedAreaMm2);
                var safetyFactor = yieldStrengthMpa / Math.Max(0.001, projectedStressMpa);

                metrics["projected_stress_mpa"] = projectedStressMpa;
                metrics["equivalent_total_load_n"] = totalLoadN;
                metrics["projected_area_mm2"] = projectedAreaMm2;
                metrics["safety_factor"] = safetyFactor;

                status = ApplySafetyFactorStatus(status, safetyFactor, recommendations);
                assumptions.Add("Unknown component accepted and analyzed via generic projected-load model.");
                recommendations.Add("Provide component_type=gear|shaft|bearing for specialized analysis.");
                break;
            }
        }

        status = RunDfmChecks(geometry, material, status, recommendations);

        if (recommendations.Count == 0)
        {
            recommendations.Add("Design is within configured stress and DFM thresholds.");
        }

        var numberedRecommendations = recommendations
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select((value, index) => $"{index + 1}. {value}")
            .ToList();

        var assumptionsUsed = orchestrationAssumptions
            .Concat(assumptions)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new SmartCadAnalysis
        {
            ComponentType = componentType,
            Status = status,
            Metrics = metrics,
            Recommendations = numberedRecommendations,
            AssumptionsUsed = assumptionsUsed
        };
    }

    public string Format(SmartCadAnalysis analysis)
    {
        var metricLines = analysis.Metrics
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => $"- {pair.Key}: {Math.Round(pair.Value, 4)}");

        var assumptionLines = analysis.AssumptionsUsed.Count == 0
            ? new[] { "- none" }
            : analysis.AssumptionsUsed.Select(value => $"- {value}");

        var sections = new[]
        {
            $"STATUS: {analysis.Status}",
            "Metrics:",
            string.Join(Environment.NewLine, metricLines),
            "Recommendations:",
            string.Join(Environment.NewLine, analysis.Recommendations),
            "assumptions_used:",
            string.Join(Environment.NewLine, assumptionLines)
        };

        return string.Join(Environment.NewLine, sections);
    }

    private static string ResolveMaterial(string? requestedMaterial, ICollection<string> assumptions)
    {
        if (!string.IsNullOrWhiteSpace(requestedMaterial) && YieldStrengthByMaterialMpa.ContainsKey(requestedMaterial.Trim()))
        {
            return requestedMaterial.Trim();
        }

        if (!string.IsNullOrWhiteSpace(requestedMaterial))
        {
            assumptions.Add($"Unsupported material '{requestedMaterial}'. Defaulted to Mild Steel.");
            return "Mild Steel";
        }

        assumptions.Add("Missing material. Defaulted to Mild Steel.");
        return "Mild Steel";
    }

    private static string ResolveComponentType(string? requestedType, string? contextType, ICollection<string> assumptions)
    {
        var candidate = !string.IsNullOrWhiteSpace(requestedType)
            ? requestedType.Trim().ToLowerInvariant()
            : !string.IsNullOrWhiteSpace(contextType)
                ? contextType.Trim().ToLowerInvariant()
                : "generic";

        if (!string.IsNullOrWhiteSpace(requestedType))
        {
            return candidate;
        }

        if (!string.IsNullOrWhiteSpace(contextType))
        {
            assumptions.Add($"component_type missing. Reused session component_type='{candidate}'.");
            return candidate;
        }

        assumptions.Add("component_type missing. Using generic projected-load model.");
        return "generic";
    }

    private static double ResolveLewisFactor(int teethCount)
    {
        var boundedTeeth = Math.Max(12, teethCount);
        var sorted = LewisFormFactorByTeeth.Keys.OrderBy(key => key).ToList();

        if (LewisFormFactorByTeeth.TryGetValue(boundedTeeth, out var exact))
        {
            return exact;
        }

        if (boundedTeeth <= sorted[0])
        {
            return LewisFormFactorByTeeth[sorted[0]];
        }

        if (boundedTeeth >= sorted[^1])
        {
            return LewisFormFactorByTeeth[sorted[^1]];
        }

        var lower = sorted.Last(value => value < boundedTeeth);
        var upper = sorted.First(value => value > boundedTeeth);
        var yLower = LewisFormFactorByTeeth[lower];
        var yUpper = LewisFormFactorByTeeth[upper];
        var t = (boundedTeeth - lower) / (double)(upper - lower);
        return yLower + ((yUpper - yLower) * t);
    }

    private static double GetValue(double? input, double fallback, ICollection<string> assumptions, string fieldName)
    {
        if (input.HasValue && input.Value > 0)
        {
            return input.Value;
        }

        assumptions.Add($"Missing or invalid {fieldName}. Defaulted to {fallback}.");
        return fallback;
    }

    private static int GetValue(int? input, int fallback, ICollection<string> assumptions, string fieldName)
    {
        if (input.HasValue && input.Value > 0)
        {
            return input.Value;
        }

        assumptions.Add($"Missing or invalid {fieldName}. Defaulted to {fallback}.");
        return fallback;
    }

    private static string ApplySafetyFactorStatus(string currentStatus, double safetyFactor, ICollection<string> recommendations)
    {
        if (safetyFactor < 1)
        {
            recommendations.Add("Increase section size, reduce load, or upgrade material; safety factor is below 1.0.");
            return Elevate(currentStatus, "FAIL");
        }

        if (safetyFactor < 1.5)
        {
            recommendations.Add("Target safety factor >= 1.5 by adjusting dimensions or material.");
            return Elevate(currentStatus, "WARNING");
        }

        return currentStatus;
    }

    private static string RunDfmChecks(GeometryInput geometry, string material, string status, ICollection<string> recommendations)
    {
        var minWallThickness = material.Equals("Nylon PA66", StringComparison.OrdinalIgnoreCase) ? 3.0 : 2.0;

        if (geometry.WallThicknessMm.HasValue)
        {
            if (geometry.WallThicknessMm.Value < minWallThickness)
            {
                status = Elevate(status, "WARNING");
                recommendations.Add($"Increase wall_thickness_mm to at least {minWallThickness} for {material}.");
            }
        }

        if (geometry.DraftAngleDeg.HasValue)
        {
            if (geometry.DraftAngleDeg.Value < 1)
            {
                status = Elevate(status, "WARNING");
                recommendations.Add("Increase draft_angle_deg to >= 1.0 for easier manufacturing release.");
            }
        }

        if (geometry.HasUndercut == true)
        {
            status = Elevate(status, "WARNING");
            recommendations.Add("Remove undercuts or plan side-actions/sliders in tooling.");
        }

        if (geometry.ThreadPitchMm.HasValue)
        {
            if (geometry.ThreadPitchMm.Value < 0.5 || geometry.ThreadPitchMm.Value > 6)
            {
                status = Elevate(status, "WARNING");
                recommendations.Add("Keep thread_pitch_mm within practical range 0.5 to 6.0 for production readiness.");
            }
        }

        return status;
    }

    private static double GetTotalLoad(LoadInput load, ICollection<string> assumptions)
    {
        if (load.AppliedLoadN.HasValue && load.AppliedLoadN.Value > 0)
        {
            return load.AppliedLoadN.Value;
        }

        var tangential = load.TangentialForceN.GetValueOrDefault(0);
        var radial = load.RadialForceN.GetValueOrDefault(0);
        var axial = load.AxialForceN.GetValueOrDefault(0);
        var sum = tangential + radial + axial;

        if (sum > 0)
        {
            assumptions.Add("applied_load_n missing. Used tangential_force_n + radial_force_n + axial_force_n.");
            return sum;
        }

        assumptions.Add("No valid load input found. Defaulted equivalent load to 1500 N.");
        return 1500;
    }

    private static string Elevate(string currentStatus, string candidate)
    {
        if (string.Equals(currentStatus, "FAIL", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(candidate, "PASS", StringComparison.OrdinalIgnoreCase))
        {
            return currentStatus;
        }

        if (string.Equals(candidate, "FAIL", StringComparison.OrdinalIgnoreCase))
        {
            return "FAIL";
        }

        if (string.Equals(currentStatus, "WARNING", StringComparison.OrdinalIgnoreCase))
        {
            return currentStatus;
        }

        return candidate;
    }
}

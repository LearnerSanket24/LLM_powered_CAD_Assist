namespace CADMCPServer.Configuration;

public sealed class AnalysisSettings
{
    public const string SectionName = "Analysis";

    public Dictionary<string, double> YieldStrengthByMaterialMpa { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Mild Steel"] = 210,
        ["Medium Carbon Steel"] = 420,
        ["Alloy Steel"] = 850,
        ["Aluminium 6061-T6"] = 276,
        ["Nylon PA66"] = 85
    };

    public Dictionary<int, double> LewisFormFactorByTeeth { get; set; } = new()
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

    public double SafetyFactorWarningThreshold { get; set; } = 1.5;
    public double SafetyFactorFailThreshold { get; set; } = 1.0;
    public double MinWallThicknessInjectionMoldingMm { get; set; } = 1.5;
    public double MinWallThicknessMetalSinteringMm { get; set; } = 0.8;
    public double MinDraftAngleDeg { get; set; } = 1.0;
    public double MinThreadPitchMm { get; set; } = 0.5;
    public double MaxThreadPitchMm { get; set; } = 6.0;
}

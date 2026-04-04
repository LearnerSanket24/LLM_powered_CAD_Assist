namespace CADMCPServer.Configuration;

public sealed class ThrottleSettings
{
    public const string SectionName = "Throttle";

    public bool Enabled { get; set; } = true;
    public int PermitLimitPerMinute { get; set; } = 120;
}

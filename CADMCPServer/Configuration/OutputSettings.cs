namespace CADMCPServer.Configuration;

public sealed class OutputSettings
{
    public const string SectionName = "Output";

    public string RootDirectory { get; set; } = "outputs";
    public bool SessionIsolationEnabled { get; set; } = true;
}

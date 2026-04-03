namespace CADMCPServer.Configuration;

public sealed class LlmSettings
{
    public const string SectionName = "Llm";

    public string Provider { get; set; } = "none";
    public string OpenAiModel { get; set; } = "gpt-4.1-mini";
    public string TogetherModel { get; set; } = "meta-llama/Meta-Llama-3.1-8B-Instruct-Turbo";
    public string OpenAiApiKey { get; set; } = string.Empty;
    public string TogetherApiKey { get; set; } = string.Empty;
    public string OpenAiBaseUrl { get; set; } = "https://api.openai.com/v1";
    public string TogetherBaseUrl { get; set; } = "https://api.together.xyz/v1";
}

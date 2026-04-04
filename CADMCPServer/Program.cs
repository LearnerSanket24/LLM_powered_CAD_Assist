using CADMCPServer.Configuration;
using CADMCPServer.Services.Assistant;
using CADMCPServer.Services.Cad;
using CADMCPServer.Services.Conversation;
using CADMCPServer.Services.Http;
using CADMCPServer.Services.Llm;
using CADMCPServer.Services.Mcp;
using CADMCPServer.Services.Planning;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<LlmSettings>(builder.Configuration.GetSection(LlmSettings.SectionName));
builder.Services.Configure<McpSettings>(builder.Configuration.GetSection(McpSettings.SectionName));
builder.Services.Configure<AnalysisSettings>(builder.Configuration.GetSection(AnalysisSettings.SectionName));
builder.Services.Configure<OutputSettings>(builder.Configuration.GetSection(OutputSettings.SectionName));
builder.Services.Configure<ThrottleSettings>(builder.Configuration.GetSection(ThrottleSettings.SectionName));

builder.Services.AddControllers();

builder.Services.AddSingleton<IConversationStore, InMemoryConversationStore>();
builder.Services.AddHttpClient<ILlmClient, FunctionCallingLlmClient>();
builder.Services.AddSingleton<ICadEngine, InMemoryCadEngine>();
builder.Services.AddSingleton<IMcpToolDispatcher, McpToolDispatcher>();
builder.Services.AddSingleton<IMcpClient, LocalMcpClient>();
builder.Services.AddSingleton<IToolPlanner, ToolPlanner>();
builder.Services.AddSingleton<ISmartCadAnalyzer, SmartCadAnalyzer>();
builder.Services.AddSingleton<IAssistantOrchestrator, AssistantOrchestrator>();

var app = builder.Build();

app.UseMiddleware<SimpleThrottleMiddleware>();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/", () => Results.Ok(new
{
    service = "CADMCPServer",
    phase = "full-llm-cad-mcp-prototype",
    status = "running"
}));

app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    utc_time = DateTimeOffset.UtcNow
}));

app.MapControllers();

app.Run();

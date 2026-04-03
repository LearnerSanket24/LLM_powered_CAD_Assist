using CADMCPServer.Configuration;
using CADMCPServer.Services.Assistant;
using CADMCPServer.Services.Conversation;
using CADMCPServer.Services.Llm;
using CADMCPServer.Services.Mcp;
using CADMCPServer.Services.Planning;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<LlmSettings>(builder.Configuration.GetSection(LlmSettings.SectionName));
builder.Services.Configure<McpSettings>(builder.Configuration.GetSection(McpSettings.SectionName));

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSingleton<IConversationStore, InMemoryConversationStore>();
builder.Services.AddHttpClient<ILlmClient, FunctionCallingLlmClient>();
builder.Services.AddHttpClient<IMcpClient, HttpMcpClient>();
builder.Services.AddSingleton<IToolPlanner, ToolPlanner>();
builder.Services.AddSingleton<IAssistantOrchestrator, AssistantOrchestrator>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/", () => Results.Ok(new
{
    service = "CADMCPServer",
    phase = "phase2-sanket-llm-layer",
    status = "running"
}));

app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    utc_time = DateTimeOffset.UtcNow
}));

app.MapControllers();

app.Run();

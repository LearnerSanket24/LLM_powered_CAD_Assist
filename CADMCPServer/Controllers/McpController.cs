using CADMCPServer.Models;
using CADMCPServer.Services.Mcp;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace CADMCPServer.Controllers;

[ApiController]
[Route("mcp")]
public sealed class McpController : ControllerBase
{
    private readonly IMcpToolDispatcher _dispatcher;

    public McpController(IMcpToolDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    [HttpPost("tool")]
    public ActionResult<McpToolResponse> ExecuteTool([FromBody] McpToolRequest request)
    {
        var response = _dispatcher.Execute(request);

        if (response.Success)
        {
            return Ok(response);
        }

        // Keep 200 for recoverable tool errors to support robust LLM retry loops.
        if (response.Error?.Recoverable == true)
        {
            return Ok(response);
        }

        var statusCode = response.StatusCode <= 0 ? 400 : response.StatusCode;
        return StatusCode(statusCode, response);
    }

    [HttpGet("schemas")]
    public IActionResult GetSchemas()
    {
        var schemaPath = Path.Combine(AppContext.BaseDirectory, "McpToolSchemas.json");
        if (!System.IO.File.Exists(schemaPath))
        {
            schemaPath = Path.Combine(Directory.GetCurrentDirectory(), "McpToolSchemas.json");
        }

        if (System.IO.File.Exists(schemaPath))
        {
            var json = System.IO.File.ReadAllText(schemaPath);
            var parsed = JsonSerializer.Deserialize<object>(json);
            return Ok(parsed);
        }

        return Ok(_dispatcher.GetSchemas());
    }
}

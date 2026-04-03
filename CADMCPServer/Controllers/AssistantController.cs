using CADMCPServer.Models;
using CADMCPServer.Services.Assistant;
using Microsoft.AspNetCore.Mvc;

namespace CADMCPServer.Controllers;

[ApiController]
[Route("assistant")]
public sealed class AssistantController : ControllerBase
{
    private readonly IAssistantOrchestrator _orchestrator;

    public AssistantController(IAssistantOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }

    [HttpPost("analyze")]
    public async Task<ActionResult<AnalyzeResponse>> Analyze([FromBody] AnalyzeRequest request, CancellationToken cancellationToken)
    {
        var response = await _orchestrator.AnalyzeAsync(request, cancellationToken);
        if (response.Status == "FAIL")
        {
            return BadRequest(response);
        }

        return Ok(response);
    }
}

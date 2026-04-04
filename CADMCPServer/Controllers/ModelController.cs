using CADMCPServer.Services.Cad;
using Microsoft.AspNetCore.Mvc;

namespace CADMCPServer.Controllers;

[ApiController]
[Route("model")]
public sealed class ModelController : ControllerBase
{
    private readonly ICadEngine _cadEngine;

    public ModelController(ICadEngine cadEngine)
    {
        _cadEngine = cadEngine;
    }

    [HttpGet("{id}")]
    public IActionResult GetModel([FromRoute] string id)
    {
        if (!_cadEngine.TryGetModel(id, out var model) || model is null)
        {
            return NotFound(new { message = "Model not found.", model_id = id });
        }

        return Ok(model.ToJson());
    }

    [HttpGet("{id}/render")]
    public IActionResult RenderModel([FromRoute] string id)
    {
        if (!_cadEngine.TryGetModel(id, out var model) || model is null)
        {
            return NotFound(new { message = "Model not found.", model_id = id });
        }

        // Prototype render metadata for frontend overlays.
        return Ok(new
        {
            model_id = model.ModelId,
            component_type = model.ComponentType,
            diameter_mm = model.EnvelopeDiameterMm,
            length_mm = model.EnvelopeLengthMm,
            hint = "Use Three.js parametric rendering on frontend using model parameters."
        });
    }
}

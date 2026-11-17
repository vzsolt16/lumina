using Lumina.Services.AI;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lumina.Controllers;

[Authorize]
[ApiController]
[Route("api/ai")]
public class AiController : ControllerBase
{
    private readonly IGeminiModelCatalog _catalog;

    public AiController(IGeminiModelCatalog catalog)
    {
        _catalog = catalog;
    }

    [HttpGet("models")]
    public IActionResult GetModels()
    {
        return Ok(new
        {
            defaultModel = _catalog.DefaultModelId,
            models = _catalog.Models.Select(model => new
            {
                id = model.Id,
                name = model.Name,
                description = model.Description
            })
        });
    }
}

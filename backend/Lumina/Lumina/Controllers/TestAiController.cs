using Lumina.Services.AI;
using Microsoft.AspNetCore.Mvc;

namespace Lumina.Controllers;

[ApiController]
[Route("api/test-ai")]
public class TestAiController : ControllerBase
{
    private readonly IAiService _aiService;

    public TestAiController(IAiService aiService)
    {
        _aiService = aiService;
    }

    [HttpGet]
    public async Task<IActionResult> Test()
    {
        var response = await _aiService.GenerateAsync(
            "What is ASP.NET Core? Answer in two sentences.");

        return Ok(response);
    }
}
using Lumina.Extensions;
using Lumina.Services.Documents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lumina.Controllers;

[Authorize]
[ApiController]
[Route("api/documents/{documentId:guid}/assets")]
public sealed class DocumentAssetsController : ControllerBase
{
    private readonly IAssetService _assets;

    public DocumentAssetsController(IAssetService assets) => _assets = assets;

    [HttpGet("{assetId:guid}")]
    public async Task<IActionResult> Get(Guid documentId, Guid assetId, CancellationToken cancellationToken)
    {
        var file = await _assets.OpenAsync(documentId, assetId, User.GetUserId(), cancellationToken);
        return file is null ? NotFound() : File(file.Content, file.ContentType, file.FileName);
    }

    [HttpPost]
    public async Task<IActionResult> Upload(
        Guid documentId, IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null) return BadRequest("No image provided.");
        try
        {
            var asset = await _assets.CreateAsync(
                documentId, file, User.GetUserId(), cancellationToken);
            return asset is null ? NotFound() : Ok(asset);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpDelete("{assetId:guid}")]
    public async Task<IActionResult> Delete(Guid documentId, Guid assetId, CancellationToken cancellationToken) =>
        await _assets.DeleteAsync(documentId, assetId, User.GetUserId(), cancellationToken)
            ? NoContent()
            : NotFound();
}

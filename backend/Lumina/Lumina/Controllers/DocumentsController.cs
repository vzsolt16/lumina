using Lumina.DTOs;
using Lumina.DTOs.Document;
using Lumina.Extensions;
using Lumina.Services.Documents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lumina.Controllers;

[Authorize]
[ApiController]
[Route("api/documents")]
public class DocumentsController : ControllerBase
{
    private readonly IDocumentService _documentService;
    private readonly ILogger<DocumentsController> _logger;

    public DocumentsController(IDocumentService documentService, ILogger<DocumentsController> logger)
    {
        _documentService = documentService;
        _logger = logger;
    }

    [HttpPost]
    public async Task<ActionResult<UploadDocumentResponse>> Upload(
        IFormFile? file,
        [FromForm] Guid? folderId,
        CancellationToken cancellationToken)
    {
        if (file is null)
        {
            return BadRequest("No file provided.");
        }

        try
        {
            var document = await _documentService.UploadAsync(
                file,
                User.GetUserId(),
                folderId,
                cancellationToken);

            return Ok(new UploadDocumentResponse
            {
                Id = document.Id,
                FileName = document.FileName
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("{id:guid}/original")]
    public async Task<IActionResult> DownloadOriginal(Guid id, CancellationToken cancellationToken)
    {
        var original = await _documentService.OpenOriginalAsync(
            id,
            User.GetUserId(),
            cancellationToken);

        if (original is null)
        {
            return NotFound();
        }

        return File(original.Content, original.ContentType, original.FileName);
    }

    [HttpGet]
    public async Task<ActionResult<List<DocumentSummaryResponse>>> GetAll()
    {
        var documents = await _documentService.GetAllAsync(User.GetUserId());

        return Ok(documents);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var document = await _documentService.GetByIdAsync(id, User.GetUserId());

        if (document is null)
        {
            return NotFound();
        }

        return Ok(document);
    }

    [HttpPatch("{id:guid}/folder")]
    public async Task<IActionResult> Move(
        Guid id,
        MoveDocumentRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var moved = await _documentService.MoveAsync(
                id,
                request.FolderId,
                User.GetUserId(),
                cancellationToken);

            if (!moved)
            {
                return NotFound();
            }

            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id,
        UpdateDocumentRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var updated = await _documentService.UpdateAsync(
                id,
                request,
                User.GetUserId(),
                cancellationToken);

            if (updated is null)
            {
                return NotFound();
            }

            return Ok(updated);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await _documentService.DeleteAsync(id, User.GetUserId(), cancellationToken);

        if (!deleted)
        {
            return NotFound();
        }

        return NoContent();
    }
}
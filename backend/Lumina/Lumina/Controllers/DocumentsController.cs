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
        IFormFile? file, [FromForm] Guid? folderId)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest("No file provided.");
        }

        const long MaxFileSize = 2 * 1024 * 1024;
        if (file.Length > MaxFileSize)
        {
            return BadRequest("File too large (max 2 MB).");
        }

        var allowedExtensions = new[] { ".txt", ".md" };

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();

        if (!allowedExtensions.Contains(extension))
        {
            return BadRequest("Only .txt and .md files are supported.");
        }

        try
        {
            var document = await _documentService.UploadAsync(file, User.GetUserId(), folderId);

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
    public async Task<IActionResult> Move(Guid id, MoveDocumentRequest request)
    {
        try
        {
            var moved = await _documentService.MoveAsync(id, request.FolderId, User.GetUserId());

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

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var deleted = await _documentService.DeleteAsync(id, User.GetUserId());

        if (!deleted)
        {
            return NotFound();
        }

        return NoContent();
    }
}
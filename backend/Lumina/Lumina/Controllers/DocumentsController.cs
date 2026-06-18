using Lumina.DTOs;
using Lumina.Services.Documents;
using Microsoft.AspNetCore.Mvc;

namespace Lumina.Controllers;

[ApiController]
[Route("api/documents")]
public class DocumentsController : ControllerBase
{
    private readonly IDocumentService _documentService;

    public DocumentsController(IDocumentService documentService)
    {
        _documentService = documentService;
    }

    [HttpPost]
    public async Task<ActionResult<UploadDocumentResponse>> Upload(
        IFormFile file)
    {
        Console.WriteLine(file.FileName);
        if (file.Length == 0)
        {
            return BadRequest("File is empty.");
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

        var document = await _documentService.UploadAsync(file);

        return Ok(new UploadDocumentResponse
        {
            Id = document.Id,
            FileName = document.FileName
        });
    }

    [HttpGet]
    public async Task<ActionResult<List<DocumentSummaryResponse>>> GetAll()
    {
        var documents = await _documentService.GetAllAsync();

        return Ok(documents);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var document = await _documentService.GetByIdAsync(id);

        if (document is null)
        {
            return NotFound();
        }

        return Ok(document);
    }
}
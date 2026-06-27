using Lumina.DTOs.Folder;
using Lumina.Extensions;
using Lumina.Services.Folders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lumina.Controllers;

[Authorize]
[ApiController]
[Route("api/folders")]
public class FoldersController : ControllerBase
{
    private readonly IFolderService _folderService;

    public FoldersController(IFolderService folderService)
    {
        _folderService = folderService;
    }

    [HttpGet]
    public async Task<ActionResult<List<FolderResponse>>> GetAll()
    {
        var folders = await _folderService.GetAllAsync(User.GetUserId());

        return Ok(folders);
    }

    [HttpPost]
    public async Task<ActionResult<FolderResponse>> Create(CreateFolderRequest request)
    {
        try
        {
            var folder = await _folderService.CreateAsync(
                User.GetUserId(), request.Name, request.ParentId);

            if (folder is null)
            {
                return NotFound("Parent folder not found.");
            }

            return Ok(folder);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<FolderResponse>> Rename(Guid id, RenameFolderRequest request)
    {
        try
        {
            var folder = await _folderService.RenameAsync(User.GetUserId(), id, request.Name);

            if (folder is null)
            {
                return NotFound();
            }

            return Ok(folder);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPatch("{id:guid}/move")]
    public async Task<ActionResult<FolderResponse>> Move(Guid id, MoveFolderRequest request)
    {
        try
        {
            var folder = await _folderService.MoveAsync(User.GetUserId(), id, request.ParentId);

            if (folder is null)
            {
                return NotFound();
            }

            return Ok(folder);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var deleted = await _folderService.DeleteAsync(User.GetUserId(), id);

        if (!deleted)
        {
            return NotFound();
        }

        return NoContent();
    }
}

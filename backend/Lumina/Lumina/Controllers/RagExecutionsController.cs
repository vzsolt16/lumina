using Lumina.Data;
using Lumina.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lumina.Controllers;

[Authorize]
[ApiController]
[Route("api/rag/executions")]
public sealed class RagExecutionsController(LuminaDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null, [FromQuery] Guid? conversationId = null,
        [FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        var query = db.RagExecutions.AsNoTracking().Where(x => x.UserId == User.GetUserId());
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(x => x.Status == status);
        if (conversationId.HasValue) query = query.Where(x => x.ConversationId == conversationId);
        if (from.HasValue) query = query.Where(x => x.StartedAt >= from.Value);
        if (to.HasValue) query = query.Where(x => x.StartedAt <= to.Value);
        var total = await query.CountAsync();
        var items = await query.OrderByDescending(x => x.StartedAt).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new { x.Id, x.ConversationId, x.DocumentId, x.StartedAt, x.Query, x.Status, x.DurationMs, x.RetrievedChunkCount, x.ModelId })
            .ToListAsync();
        return Ok(new { items, page, pageSize, total });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var execution = await db.RagExecutions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.UserId == User.GetUserId());
        return execution is null ? NotFound() : Ok(execution);
    }
}

using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Services.Auth;
using Hatch.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// The board-wide table of Ollama models: an id, its context length and its
/// memory footprint - what <see cref="LocalModelFit"/> reads to say which ids
/// fit an Ollama runner's own machine.
/// </summary>
/// <remarks>
/// Reading is open to an API key, the same as <see cref="PlaybooksController"/>:
/// a runner's own heartbeat has to know the table to be told which ids it holds.
/// Writing is a person's alone, at a browser - <see cref="PlaybookWrites.Refusal"/>,
/// the same three-line guard <see cref="PlaybooksController"/> carries on each of
/// its own writes.
/// </remarks>
[ApiController]
[Route("api/local-models")]
public class LocalModelsController(HatchContext db, TimeProvider time, ICallerIdentity caller, ModelFamilies? families = null) : ControllerBase
{
    /// <summary>Every row, in creation order.</summary>
    [HttpGet]
    [RequireRole(PersonRole.User, AcceptKey = true)]
    public async Task<ActionResult<IReadOnlyList<LocalModelDto>>> GetLocalModels(CancellationToken ct)
    {
        var rows = await db.LocalModels.AsNoTracking()
            .OrderBy(m => m.CreatedAt).ThenBy(m => m.Id)
            .ToListAsync(ct);

        return rows.Select(ToDto).ToList();
    }

    [HttpPost]
    [RequireRole(PersonRole.User)]
    public async Task<ActionResult<LocalModelDto>> CreateLocalModel(LocalModelCreateRequest request, CancellationToken ct)
    {
        if (await PlaybookWrites.Refusal(caller, Request, ct) is { } refusal) return refusal;

        var ollamaId = request.OllamaId?.Trim() ?? "";
        if (PlaybookFile.InvalidLocalModel(ollamaId, families) is { } idError) return BadRequest(idError);

        if (await db.LocalModels.AnyAsync(m => m.OllamaId == ollamaId, ct))
            return Conflict($"there is already a local model \"{ollamaId}\"");

        if (request.ContextLengthTokens <= 0) return BadRequest("a context length is a positive number of tokens");
        if (request.MemoryFootprintBytes <= 0) return BadRequest("a memory footprint is a positive number of bytes");

        var model = new EfHatchLocalModel
        {
            OllamaId = ollamaId,
            ContextLengthTokens = request.ContextLengthTokens,
            MemoryFootprintBytes = request.MemoryFootprintBytes,
            CreatedBy = await caller.ActorNameAsync(ct),
            CreatedAt = time.GetUtcNow(),
        };

        db.LocalModels.Add(model);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetLocalModels), ToDto(model));
    }

    [HttpDelete("{id:int}")]
    [RequireRole(PersonRole.User)]
    public async Task<IActionResult> DeleteLocalModel(int id, CancellationToken ct)
    {
        if (await PlaybookWrites.Refusal(caller, Request, ct) is { } refusal) return refusal;

        var model = await db.LocalModels.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (model is null) return NotFound();

        db.LocalModels.Remove(model);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static LocalModelDto ToDto(EfHatchLocalModel m) =>
        new(m.Id, m.OllamaId, m.ContextLengthTokens, m.MemoryFootprintBytes, m.CreatedBy, m.CreatedAt);
}

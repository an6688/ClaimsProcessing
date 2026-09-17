using Claims.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Claims.Api;

public sealed record HealthResponse(string Status);

[ApiController]
public sealed class HealthController(ClaimsDbContext db) : ControllerBase
{
    [HttpGet("/api/health")]
    [ProducesResponseType<HealthResponse>(200, "application/json")]
    [ProducesResponseType<HealthResponse>(503, "application/json")]
    public async Task<ActionResult<HealthResponse>> Get(CancellationToken ct)
    {
        try
        {
            await db.Members.AsNoTracking().Select(m => m.Id).Take(1).ToListAsync(ct);
            return Ok(new HealthResponse("Healthy"));
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return StatusCode(503, new HealthResponse("Unhealthy"));
        }
    }
}


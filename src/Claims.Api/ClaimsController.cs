using System.ComponentModel.DataAnnotations;
using Claims.Application;
using Claims.Domain;
using Microsoft.AspNetCore.Mvc;

namespace Claims.Api;

[ApiController]
[Route("api/claims")]
[ProducesResponseType<ProblemDetails>(500, "application/problem+json")]
public sealed class ClaimsController(ClaimService service) : ControllerBase
{
    [HttpPost]
    [RequestSizeLimit(128 * 1024)]
    [ProducesResponseType<ClaimResponse>(201, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(400, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(404, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(413, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(415, "application/problem+json")]
    public async Task<ActionResult<ClaimResponse>> Submit(SubmitClaimRequest request, CancellationToken ct)
    {
        var claim = await service.Submit(request, ct);
        return CreatedAtAction(nameof(Get), new { id = claim.Id }, claim);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ClaimResponse>(200, "application/json")]
    [ProducesResponseType<ProblemDetails>(404, "application/problem+json")]
    public async Task<ActionResult<ClaimResponse>> Get(Guid id, CancellationToken ct) =>
        await service.Get(id, ct);

    [HttpPost("{id:guid}/process")]
    [ProducesResponseType<ClaimResponse>(200, "application/json")]
    [ProducesResponseType<ProblemDetails>(404, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(409, "application/problem+json")]
    public async Task<ActionResult<ClaimResponse>> Process(Guid id, CancellationToken ct) =>
        await service.Process(id, ct);

    [HttpGet]
    [ProducesResponseType<Page<ClaimResponse>>(200, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(400, "application/problem+json")]
    public async Task<ActionResult<Page<ClaimResponse>>> List(CancellationToken ct,
        [FromQuery] ClaimStatus? status = null,
        [FromQuery, Range(1, 1000000)] int page = 1,
        [FromQuery, Range(1, 100)] int pageSize = 20) =>
        await service.List(null, status, page, pageSize, ct);

    [HttpGet("/api/members/{id:guid}/claims")]
    [ProducesResponseType<Page<ClaimResponse>>(200, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(400, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(404, "application/problem+json")]
    public async Task<ActionResult<Page<ClaimResponse>>> MemberClaims(Guid id, CancellationToken ct,
        [FromQuery, Range(1, 1000000)] int page = 1,
        [FromQuery, Range(1, 100)] int pageSize = 20) =>
        await service.List(id, null, page, pageSize, ct);
}


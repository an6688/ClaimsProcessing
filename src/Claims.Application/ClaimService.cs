using Claims.Domain;

namespace Claims.Application;

public sealed class ClaimService(IClaimRepository repository, TimeProvider clock)
{
    public async Task<ClaimResponse> Submit(SubmitClaimRequest request, CancellationToken ct)
    {
        Claim claim;
        try
        {
            if (!request.MemberId.HasValue || !request.ProviderId.HasValue)
                throw new ArgumentException("Member and provider IDs are required.");
            if (request.Lines is null || request.Lines.Any(l => l is null))
                throw new ArgumentException("Claim lines are required and cannot contain null entries.");
            if (request.Lines.Any(l => !l.ServiceDate.HasValue || !l.Quantity.HasValue || !l.UnitPrice.HasValue))
                throw new ArgumentException("Service date, quantity and unit price are required.");
            claim = Claim.Submit(request.MemberId.Value, request.ProviderId.Value,
                request.Lines.Select(l => ClaimLine.Create(
                    l.ProcedureCode, l.ServiceDate!.Value, l.Quantity!.Value, l.UnitPrice!.Value)),
                clock.GetUtcNow());
        }
        catch (ArgumentException ex)
        {
            throw new RequestValidationException(ex.Message);
        }

        if (!await repository.MemberExists(claim.MemberId, ct))
            throw new ResourceNotFoundException("Member was not found.");
        if (!await repository.ProviderExists(claim.ProviderId, ct))
            throw new ResourceNotFoundException("Provider was not found.");
        await repository.Add(claim, ct);
        return ClaimResponse.From(claim);
    }

    public async Task<ClaimResponse> Get(Guid id, CancellationToken ct) => ClaimResponse.From(
        await repository.Get(id, ct) ?? throw new ResourceNotFoundException("Claim was not found."));

    public async Task<ClaimResponse> Process(Guid id, CancellationToken ct) => ClaimResponse.From(
        await repository.Process(id, clock.GetUtcNow(), ct)
            ?? throw new ResourceNotFoundException("Claim was not found."));

    public async Task<Page<ClaimResponse>> List(
        Guid? memberId, ClaimStatus? status, int page, int pageSize, CancellationToken ct)
    {
        if (page is < 1 or > 1000000 || pageSize is < 1 or > 100)
            throw new RequestValidationException("Page must be 1 to 1000000 and pageSize 1 to 100.");
        if (status.HasValue && !Enum.IsDefined(status.Value))
            throw new RequestValidationException("Unknown claim status.");
        if (memberId.HasValue && !await repository.MemberExists(memberId.Value, ct))
            throw new ResourceNotFoundException("Member was not found.");
        var result = await repository.List(memberId, status, page, pageSize, ct);
        return new(result.Items.Select(ClaimResponse.From).ToList(), page, pageSize, result.TotalCount);
    }
}


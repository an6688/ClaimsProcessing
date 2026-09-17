using System.ComponentModel.DataAnnotations;
using Claims.Domain;

namespace Claims.Application;

public sealed class SubmitClaimRequest
{
    [Required]
    public Guid? MemberId { get; init; }
    [Required]
    public Guid? ProviderId { get; init; }
    [Required, MinLength(1), MaxLength(100)]
    public List<ClaimLineRequest> Lines { get; init; } = [];
}

public sealed class ClaimLineRequest
{
    [Required, RegularExpression(@"^[A-Za-z0-9-]{1,20}$")]
    public string ProcedureCode { get; init; } = "";
    [Required]
    public DateOnly? ServiceDate { get; init; }
    [Required, Range(1, 1000)]
    public int? Quantity { get; init; }
    [Required, Range(typeof(decimal), "0.01", "1000000")]
    public decimal? UnitPrice { get; init; }
}

public sealed record ClaimLineResponse(Guid Id, string ProcedureCode, DateOnly ServiceDate,
    int Quantity, decimal UnitPrice, decimal Amount);

public sealed record DenialReasonResponse(DenialCode Code, string Message, Guid? DuplicateClaimId);

public sealed record ClaimResponse(Guid Id, Guid MemberId, Guid ProviderId, ClaimStatus Status,
    DateTimeOffset SubmittedAt, decimal TotalAmount, IReadOnlyList<ClaimLineResponse> Lines,
    DateTimeOffset? ValidatedAt, DateTimeOffset? ProcessedAt, DenialReasonResponse? DenialReason)
{
    public static ClaimResponse From(Claim claim) => new(
        claim.Id, claim.MemberId, claim.ProviderId, claim.Status, claim.SubmittedAt, claim.TotalAmount,
        claim.Lines.OrderBy(l => l.Id).Select(l => new ClaimLineResponse(
            l.Id, l.ProcedureCode, l.ServiceDate, l.Quantity, l.UnitPrice, l.Amount)).ToList(),
        claim.ValidatedAt, claim.ProcessedAt,
        claim.DenialCode.HasValue
            ? new DenialReasonResponse(claim.DenialCode.Value, claim.DenialMessage!, claim.DuplicateClaimId)
            : null);
}

public sealed record Page<T>(IReadOnlyList<T> Items, int PageNumber, int PageSize, int TotalCount);
public sealed class ResourceNotFoundException(string message) : Exception(message);
public sealed class RequestValidationException(string message) : Exception(message);
public sealed class ProcessingConflictException(string message) : Exception(message);

public interface IClaimRepository
{
    Task<bool> MemberExists(Guid id, CancellationToken ct);
    Task<bool> ProviderExists(Guid id, CancellationToken ct);
    Task Add(Claim claim, CancellationToken ct);
    Task<Claim?> Get(Guid id, CancellationToken ct);
    Task<Claim?> Process(Guid id, DateTimeOffset now, CancellationToken ct);
    Task<Page<Claim>> List(Guid? memberId, ClaimStatus? status, int page, int pageSize, CancellationToken ct);
}


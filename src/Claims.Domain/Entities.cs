namespace Claims.Domain;

public enum ClaimStatus { Submitted, Validated, Approved, Denied }

public enum DenialCode
{
    MemberNotFound, ProviderNotFound, InactiveProvider, InactiveCoverage,
    FutureServiceDate, InvalidServiceLine, EmptyClaim, DuplicateClaim
}

public sealed class Member
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string MemberNumber { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateOnly CoverageStart { get; set; } = new(2020, 1, 1);
    public DateOnly? CoverageEnd { get; set; }

    public bool Covers(DateOnly date) =>
        IsActive && date >= CoverageStart && (!CoverageEnd.HasValue || date <= CoverageEnd.Value);
}

public sealed class Provider
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string ProviderNumber { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

public sealed class Claim
{
    private Claim() { }
    public Guid Id { get; private set; }
    public Guid MemberId { get; private set; }
    public Guid ProviderId { get; private set; }
    public ClaimStatus Status { get; private set; }
    public DateTimeOffset SubmittedAt { get; private set; }
    public DateTimeOffset? ValidatedAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }
    public DenialCode? DenialCode { get; private set; }
    public string? DenialMessage { get; private set; }
    public Guid? DuplicateClaimId { get; private set; }
    private readonly List<ClaimLine> _lines = [];
    public IReadOnlyCollection<ClaimLine> Lines => _lines.AsReadOnly();
    public decimal TotalAmount => _lines.Sum(line => line.Amount);

    public static Claim Submit(Guid memberId, Guid providerId, IEnumerable<ClaimLine> lines, DateTimeOffset now)
    {
        if (memberId == Guid.Empty || providerId == Guid.Empty)
            throw new ArgumentException("Member and provider IDs are required.");
        ArgumentNullException.ThrowIfNull(lines);
        var items = lines.ToList();
        if (items.Count is < 1 or > 100)
            throw new ArgumentException("A claim requires 1 to 100 lines.");
        if (items.Any(line => line is null))
            throw new ArgumentException("Claim lines cannot contain null entries.");
        if (items.Any(line => line.ServiceDate > DateOnly.FromDateTime(now.UtcDateTime)))
            throw new ArgumentException("Service dates cannot be in the future.");
        if (items.Select(line => line.Id).Distinct().Count() != items.Count)
            throw new ArgumentException("Claim lines must be distinct.");
        var claim = new Claim
        {
            Id = Guid.NewGuid(), MemberId = memberId, ProviderId = providerId,
            Status = ClaimStatus.Submitted, SubmittedAt = now
        };
        claim._lines.AddRange(items);
        return claim;
    }

    public void Process(Member? member, Provider? provider, Guid? duplicateClaimId, DateTimeOffset now)
    {
        // Decisions are immutable. A retry returns the original outcome and timestamps.
        if (Status is ClaimStatus.Approved or ClaimStatus.Denied) return;

        if (_lines.Count == 0)
        {
            Deny(global::Claims.Domain.DenialCode.EmptyClaim, "A claim requires at least one service line.", now);
            return;
        }
        if (_lines.Any(line => line.Amount <= 0 || !ClaimLine.IsValidProcedureCode(line.ProcedureCode) ||
                              line.ServiceDate == default))
        {
            Deny(global::Claims.Domain.DenialCode.InvalidServiceLine, "A service line is invalid.", now);
            return;
        }
        if (_lines.Any(line => line.ServiceDate > DateOnly.FromDateTime(now.UtcDateTime)))
        {
            Deny(global::Claims.Domain.DenialCode.FutureServiceDate, "Service dates cannot be in the future.", now);
            return;
        }

        // Validation and the final decision are persisted atomically by the repository.
        Status = ClaimStatus.Validated;
        ValidatedAt = now;

        if (member is null || member.Id != MemberId)
            Deny(global::Claims.Domain.DenialCode.MemberNotFound, "Member was not found.", now);
        else if (provider is null || provider.Id != ProviderId)
            Deny(global::Claims.Domain.DenialCode.ProviderNotFound, "Provider was not found.", now);
        else if (!provider.IsActive)
            Deny(global::Claims.Domain.DenialCode.InactiveProvider, "Provider is inactive.", now);
        else if (_lines.Any(line => !member.Covers(line.ServiceDate)))
            Deny(global::Claims.Domain.DenialCode.InactiveCoverage, "Member coverage is not active for every service date.", now);
        else if (duplicateClaimId.HasValue)
        {
            DuplicateClaimId = duplicateClaimId;
            Deny(global::Claims.Domain.DenialCode.DuplicateClaim, "A processed claim already contains the same service.", now);
        }
        else
        {
            Status = ClaimStatus.Approved;
            ProcessedAt = now;
        }
    }

    private void Deny(DenialCode code, string message, DateTimeOffset now)
    {
        Status = ClaimStatus.Denied;
        DenialCode = code;
        DenialMessage = message;
        ProcessedAt = now;
    }
}

public sealed class ClaimLine
{
    private ClaimLine() { }
    public Guid Id { get; private set; }
    public Guid ClaimId { get; private set; }
    public string ProcedureCode { get; private set; } = "";
    public DateOnly ServiceDate { get; private set; }
    public int Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal Amount => Quantity * UnitPrice;

    public static bool IsValidProcedureCode(string? code) =>
        !string.IsNullOrWhiteSpace(code) && code.Length <= 20 &&
        code.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');

    public static ClaimLine Create(string procedureCode, DateOnly serviceDate, int quantity, decimal unitPrice)
    {
        if (!IsValidProcedureCode(procedureCode))
            throw new ArgumentException("Procedure code must contain 1 to 20 letters, digits or hyphens.");
        if (serviceDate == default) throw new ArgumentException("Service date is required.");
        if (quantity is < 1 or > 1000) throw new ArgumentException("Quantity must be between 1 and 1000.");
        if (unitPrice <= 0 || unitPrice > 1000000 || decimal.Round(unitPrice, 2) != unitPrice)
            throw new ArgumentException("Unit price must be positive, at most 1000000, and have at most two decimal places.");
        return new ClaimLine
        {
            Id = Guid.NewGuid(), ProcedureCode = procedureCode.ToUpperInvariant(),
            ServiceDate = serviceDate, Quantity = quantity, UnitPrice = unitPrice
        };
    }
}


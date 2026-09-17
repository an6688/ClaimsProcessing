using Claims.Domain;
using Xunit;

namespace Claims.UnitTests;

public class ProcessingTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 10, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("2025-01-01", true)]
    [InlineData("2025-12-31", true)]
    [InlineData("2024-12-31", false)]
    [InlineData("2026-01-01", false)]
    public void CoverageBoundariesAreInclusive(string date, bool approved)
    {
        var member = new Member { Id = Guid.NewGuid(), CoverageStart = new(2025, 1, 1), CoverageEnd = new(2025, 12, 31) };
        var provider = new Provider { Id = Guid.NewGuid() };
        var claim = Claim.Submit(member.Id, provider.Id,
            [ClaimLine.Create("SYN-01", DateOnly.Parse(date), 1, 1)], Now);

        claim.Process(member, provider, null, Now);

        Assert.Equal(approved ? ClaimStatus.Approved : ClaimStatus.Denied, claim.Status);
        Assert.Equal(approved ? null : (DenialCode?)DenialCode.InactiveCoverage, claim.DenialCode);
        Assert.Equal(Now, claim.ValidatedAt);
        Assert.Equal(Now, claim.ProcessedAt);
    }

    [Theory]
    [InlineData("member", DenialCode.MemberNotFound)]
    [InlineData("provider", DenialCode.ProviderNotFound)]
    [InlineData("inactive-member", DenialCode.InactiveCoverage)]
    [InlineData("inactive-provider", DenialCode.InactiveProvider)]
    [InlineData("duplicate", DenialCode.DuplicateClaim)]
    public void FailedRuleProducesStructuredDecision(string scenario, DenialCode expected)
    {
        var member = new Member { Id = Guid.NewGuid(), IsActive = scenario != "inactive-member" };
        var provider = new Provider { Id = Guid.NewGuid(), IsActive = scenario != "inactive-provider" };
        var duplicateId = scenario == "duplicate" ? Guid.NewGuid() : (Guid?)null;
        var claim = Claim.Submit(member.Id, provider.Id,
            [ClaimLine.Create("SYN-01", new(2025, 1, 1), 1, 1)], Now);

        claim.Process(scenario == "member" ? null : member,
            scenario == "provider" ? null : provider, duplicateId, Now);

        Assert.Equal(ClaimStatus.Denied, claim.Status);
        Assert.Equal(expected, claim.DenialCode);
        Assert.False(string.IsNullOrWhiteSpace(claim.DenialMessage));
        Assert.Equal(duplicateId, claim.DuplicateClaimId);
    }

    [Fact]
    public void OneUncoveredLineDeniesTheWholeClaim()
    {
        var member = new Member { Id = Guid.NewGuid(), CoverageEnd = new(2024, 12, 31) };
        var provider = new Provider { Id = Guid.NewGuid() };
        var claim = Claim.Submit(member.Id, provider.Id,
            [ClaimLine.Create("A", new(2024, 12, 31), 1, 1), ClaimLine.Create("B", new(2025, 1, 1), 1, 1)], Now);
        claim.Process(member, provider, null, Now);
        Assert.Equal(DenialCode.InactiveCoverage, claim.DenialCode);
    }

    [Fact]
    public void DecisionCannotBeChangedByReprocessing()
    {
        var member = new Member { Id = Guid.NewGuid() };
        var provider = new Provider { Id = Guid.NewGuid() };
        var claim = Claim.Submit(member.Id, provider.Id, [ClaimLine.Create("A", new(2025, 1, 1), 1, 1)], Now);
        claim.Process(member, provider, null, Now);
        provider.IsActive = false;
        claim.Process(member, provider, Guid.NewGuid(), Now.AddDays(1));
        Assert.Equal(ClaimStatus.Approved, claim.Status);
        Assert.Equal(Now, claim.ProcessedAt);
        Assert.Null(claim.DenialCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("contains space")]
    [InlineData("é")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTU")]
    public void InvalidProcedureCodesAreRejected(string code) =>
        Assert.Throws<ArgumentException>(() => ClaimLine.Create(code, new(2025, 1, 1), 1, 1));
}


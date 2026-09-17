using Claims.Domain;
using Xunit;
namespace Claims.UnitTests;

public class ClaimTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 10, 0, 0, 0, TimeSpan.Zero);
    [Fact]
    public void SubmissionCalculatesTotalAndStartsSubmitted()
    {
        var claim = Claim.Submit(Guid.NewGuid(), Guid.NewGuid(),
            [ClaimLine.Create("SYN-01", new(2026, 1, 1), 3, 12.25m)], Now);
        Assert.Equal(36.75m, claim.TotalAmount);
        Assert.Equal(ClaimStatus.Submitted, claim.Status);
        Assert.Equal(Now, claim.SubmittedAt);
    }
    [Fact]
    public void EmptyClaimIsRejected() => Assert.Throws<ArgumentException>(() =>
        Claim.Submit(Guid.NewGuid(), Guid.NewGuid(), [], Now));
    [Fact]
    public void FutureServiceDateIsRejected() => Assert.Throws<ArgumentException>(() =>
        Claim.Submit(Guid.NewGuid(), Guid.NewGuid(), [ClaimLine.Create("SYN-01", new(2026, 1, 11), 1, 1)], Now));
    [Theory]
    [InlineData(0, 10)] [InlineData(1001, 10)] [InlineData(1, 0)] [InlineData(1, -1)]
    [InlineData(1, 1.001)] [InlineData(1, 1000001)]
    public void InvalidChargesAreRejected(int quantity, decimal price) => Assert.Throws<ArgumentException>(() =>
        ClaimLine.Create("SYN-01", new(2026, 1, 1), quantity, price));
}

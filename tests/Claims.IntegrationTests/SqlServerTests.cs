using Claims.Domain;
using Claims.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;
namespace Claims.IntegrationTests;

public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CLAIMS_TEST_SQLSERVER")))
            Skip = "Set CLAIMS_TEST_SQLSERVER to run the SQL Server migration test.";
    }
}
public class SqlServerTests
{
    [SqlServerFact]
    public async Task MigrationsSeedAndRepositoryWorkOnSqlServer()
    {
        var connection = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("CLAIMS_TEST_SQLSERVER"))
            { InitialCatalog = $"ClaimsTests_{Guid.NewGuid():N}" };
        await using var db = new ClaimsDbContext(new DbContextOptionsBuilder<ClaimsDbContext>()
            .UseSqlServer(connection.ConnectionString).Options);
        try
        {
            await db.Database.MigrateAsync();
            Assert.False(db.Database.HasPendingModelChanges());
            await DevelopmentSeeder.Seed(db);
            await DevelopmentSeeder.Seed(db);
            Assert.Equal(3, await db.Members.CountAsync());
            var claim = Claim.Submit((await db.Members.FirstAsync()).Id, (await db.Providers.FirstAsync()).Id,
                [ClaimLine.Create("SYN-01", new(2025, 1, 1), 2, 3.50m)], DateTimeOffset.UtcNow);
            var repo = new ClaimRepository(db);
            await repo.Add(claim, default);
            db.ChangeTracker.Clear();
            Assert.Equal(7m, (await repo.Get(claim.Id, default))!.TotalAmount);
            Assert.Single((await repo.List(null, ClaimStatus.Submitted, 1, 20, default)).Items);
            Assert.Equal(ClaimStatus.Approved,
                (await repo.Process(claim.Id, DateTimeOffset.UtcNow, default))!.Status);

            var memberId = claim.MemberId;
            var providerId = claim.ProviderId;
            var concurrentClaims = new[]
            {
                Claim.Submit(memberId, providerId,
                    [ClaimLine.Create("SYN-CONCURRENT", new(2025, 2, 1), 1, 5m)], DateTimeOffset.UtcNow),
                Claim.Submit(memberId, providerId,
                    [ClaimLine.Create("syn-concurrent", new(2025, 2, 1), 2, 8m)], DateTimeOffset.UtcNow)
            };
            await repo.Add(concurrentClaims[0], default);
            await repo.Add(concurrentClaims[1], default);
            db.ChangeTracker.Clear();

            async Task<ClaimStatus> ProcessWithSeparateContext(Guid id)
            {
                await using var context = new ClaimsDbContext(new DbContextOptionsBuilder<ClaimsDbContext>()
                    .UseSqlServer(connection.ConnectionString).Options);
                return (await new ClaimRepository(context).Process(
                    id, DateTimeOffset.UtcNow, default))!.Status;
            }
            var decisions = await Task.WhenAll(concurrentClaims.Select(c => ProcessWithSeparateContext(c.Id)));
            Assert.Single(decisions, status => status == ClaimStatus.Approved);
            Assert.Single(decisions, status => status == ClaimStatus.Denied);
        }
        finally { await db.Database.EnsureDeletedAsync(); }
    }
}

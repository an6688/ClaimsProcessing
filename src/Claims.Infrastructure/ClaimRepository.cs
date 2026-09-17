using Claims.Application;
using Claims.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Claims.Infrastructure;

public sealed class ClaimRepository(ClaimsDbContext db) : IClaimRepository
{
    public Task<bool> MemberExists(Guid id, CancellationToken ct) =>
        db.Members.AnyAsync(m => m.Id == id, ct);

    public Task<bool> ProviderExists(Guid id, CancellationToken ct) =>
        db.Providers.AnyAsync(p => p.Id == id, ct);

    public async Task Add(Claim claim, CancellationToken ct)
    {
        db.Claims.Add(claim);
        await db.SaveChangesAsync(ct); // One transaction for the claim and all its lines.
    }

    public Task<Claim?> Get(Guid id, CancellationToken ct) =>
        db.Claims.AsNoTracking().Include(c => c.Lines).SingleOrDefaultAsync(c => c.Id == id, ct);

    public async Task<Claim?> Process(Guid id, DateTimeOffset now, CancellationToken ct)
    {
        var memberId = await db.Claims.Where(c => c.Id == id).Select(c => (Guid?)c.MemberId)
            .SingleOrDefaultAsync(ct);
        if (!memberId.HasValue) return null;

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            // Serialize processing for a member across API instances. Lock BEFORE reading
            // the claim or checking duplicates, and hold through decision persistence.
            // SQLite is used only by sequential HTTP tests; SQL Server tests cover races.
            var member = db.Database.IsSqlServer()
                ? await db.Members.FromSqlInterpolated(
                    $"SELECT * FROM [Members] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {memberId.Value}")
                    .AsNoTracking().SingleOrDefaultAsync(ct)
                : await db.Members.AsNoTracking().SingleOrDefaultAsync(m => m.Id == memberId.Value, ct);

            var claim = await db.Claims.Include(c => c.Lines).SingleOrDefaultAsync(c => c.Id == id, ct);
            if (claim is null) return null;
            if (claim.Status is ClaimStatus.Approved or ClaimStatus.Denied)
            {
                await transaction.CommitAsync(ct);
                return claim;
            }

            var provider = await db.Providers.AsNoTracking()
                .SingleOrDefaultAsync(p => p.Id == claim.ProviderId, ct);
            var duplicateId = await FindDuplicate(claim, ct);
            claim.Process(member, provider, duplicateId, now);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return claim;
        }
        catch (SqlException ex) when (ex.Number is 1205 or 1222)
        {
            throw new ProcessingConflictException("Processing conflicted with another request. Retry this claim.");
        }
    }

    private async Task<Guid?> FindDuplicate(Claim claim, CancellationToken ct)
    {
        var processed = db.Claims.Where(c => c.Id != claim.Id &&
            c.MemberId == claim.MemberId && c.ProviderId == claim.ProviderId &&
            (c.Status == ClaimStatus.Approved || c.Status == ClaimStatus.Denied));
        foreach (var line in claim.Lines.DistinctBy(l => (l.ServiceDate, l.ProcedureCode.ToUpperInvariant())))
        {
            var code = line.ProcedureCode.ToUpperInvariant();
            // Compare normalized codes even for rows written by the previous version.
            var match = await processed.Where(c => c.Lines.Any(l =>
                    l.ServiceDate == line.ServiceDate && l.ProcedureCode.ToUpper() == code))
                .OrderBy(c => c.SubmittedAt).ThenBy(c => c.Id)
                .Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);
            if (match.HasValue) return match;
        }
        return null;
    }

    public async Task<Page<Claim>> List(
        Guid? memberId, ClaimStatus? status, int page, int pageSize, CancellationToken ct)
    {
        var query = db.Claims.AsNoTracking().AsQueryable();
        if (memberId.HasValue) query = query.Where(c => c.MemberId == memberId.Value);
        if (status.HasValue) query = query.Where(c => c.Status == status.Value);
        var count = await query.CountAsync(ct);
        var items = await query.OrderByDescending(c => c.SubmittedAt).ThenBy(c => c.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).Include(c => c.Lines).ToListAsync(ct);
        return new(items, page, pageSize, count);
    }
}


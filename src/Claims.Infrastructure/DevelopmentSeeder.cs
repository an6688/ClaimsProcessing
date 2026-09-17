using Claims.Domain;
using Microsoft.EntityFrameworkCore;

namespace Claims.Infrastructure;

public static class DevelopmentSeeder
{
    public static async Task Seed(ClaimsDbContext db, CancellationToken ct = default)
    {
        for (var i = 1; i <= 3; i++)
        {
            var memberId = Guid.Parse($"10000000-0000-0000-0000-{i:000000000000}");
            var providerId = Guid.Parse($"20000000-0000-0000-0000-{i:000000000000}");
            var member = await db.Members.SingleOrDefaultAsync(m => m.Id == memberId, ct);
            if (member is null)
            {
                member = new Member { Id = memberId };
                db.Members.Add(member);
            }
            member.MemberNumber = $"SYN-M{i:000}";
            member.Name = $"Synthetic Member {i}";
            member.IsActive = i != 3;
            member.CoverageStart = new(2020, 1, 1);
            member.CoverageEnd = i == 2 ? new DateOnly(2024, 12, 31) : null;

            var provider = await db.Providers.SingleOrDefaultAsync(p => p.Id == providerId, ct);
            if (provider is null)
            {
                provider = new Provider { Id = providerId };
                db.Providers.Add(provider);
            }
            provider.ProviderNumber = $"SYN-P{i:000}";
            provider.Name = $"Synthetic Clinic {i}";
            provider.IsActive = i != 3;
        }
        await db.SaveChangesAsync(ct);
    }
}

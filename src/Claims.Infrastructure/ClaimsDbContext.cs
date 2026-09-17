using Claims.Domain;
using Microsoft.EntityFrameworkCore;
namespace Claims.Infrastructure;

public sealed class ClaimsDbContext(DbContextOptions<ClaimsDbContext> options) : DbContext(options)
{
    public DbSet<Member> Members => Set<Member>();
    public DbSet<Provider> Providers => Set<Provider>();
    public DbSet<Claim> Claims => Set<Claim>();
    public DbSet<ClaimLine> ClaimLines => Set<ClaimLine>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Member>(b => {
            b.HasKey(x => x.Id); b.Property(x => x.Name).HasMaxLength(120).IsRequired();
            b.Property(x => x.MemberNumber).HasMaxLength(30).IsRequired(); b.HasIndex(x => x.MemberNumber).IsUnique();
            b.ToTable(t => t.HasCheckConstraint("CK_Members_Coverage", "[CoverageEnd] IS NULL OR [CoverageEnd] >= [CoverageStart]"));
        });
        model.Entity<Provider>(b => {
            b.HasKey(x => x.Id); b.Property(x => x.Name).HasMaxLength(120).IsRequired();
            b.Property(x => x.ProviderNumber).HasMaxLength(30).IsRequired(); b.HasIndex(x => x.ProviderNumber).IsUnique();
        });
        model.Entity<Claim>(b => {
            b.HasKey(x => x.Id); b.Ignore(x => x.TotalAmount);
            // Persist UTC instants consistently; this also supports ordering in SQLite tests.
            b.Property(x => x.SubmittedAt).HasConversion(
                value => value.UtcDateTime,
                value => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)));
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            b.Property(x => x.ValidatedAt).HasConversion(
                value => value.HasValue ? value.Value.UtcDateTime : (DateTime?)null,
                value => value.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)) : null);
            b.Property(x => x.ProcessedAt).HasConversion(
                value => value.HasValue ? value.Value.UtcDateTime : (DateTime?)null,
                value => value.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)) : null);
            b.Property(x => x.DenialCode).HasConversion<string>().HasMaxLength(40);
            b.Property(x => x.DenialMessage).HasMaxLength(240);
            b.HasOne<Member>().WithMany().HasForeignKey(x => x.MemberId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Provider>().WithMany().HasForeignKey(x => x.ProviderId).OnDelete(DeleteBehavior.Restrict);
            b.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.ClaimId).OnDelete(DeleteBehavior.Cascade);
            b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
            b.HasIndex(x => new { x.Status, x.SubmittedAt });
            b.HasIndex(x => new { x.MemberId, x.SubmittedAt });
            b.ToTable(t => t.HasCheckConstraint("CK_Claims_Status", "[Status] IN ('Submitted','Validated','Approved','Denied')"));
        });
        model.Entity<ClaimLine>(b => {
            b.HasKey(x => x.Id); b.Ignore(x => x.Amount);
            b.Property(x => x.ProcedureCode).HasMaxLength(20).IsRequired();
            b.Property(x => x.UnitPrice).HasPrecision(18, 2).HasColumnType("decimal(18,2)");
            b.ToTable(t => {
                t.HasCheckConstraint("CK_ClaimLines_Quantity", "[Quantity] BETWEEN 1 AND 1000");
                t.HasCheckConstraint("CK_ClaimLines_UnitPrice", "[UnitPrice] > 0 AND [UnitPrice] <= 1000000");
            });
        });
    }
}

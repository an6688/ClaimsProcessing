using Claims.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace Claims.IntegrationTests;

public sealed class ApiFactory(string environment = "Testing") : WebApplicationFactory<Program>
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        connection.Open();
        builder.UseEnvironment(environment);
        builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:Claims"] = "unused",
                ["Database:Initialize"] = "false"
            }));
        builder.ConfigureServices(services => {
            services.RemoveAll<DbContextOptions<ClaimsDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<ClaimsDbContext>>();
            services.AddDbContext<ClaimsDbContext>(o => o.UseSqlite(connection));
            services.Replace(ServiceDescriptor.Singleton<TimeProvider>(new FixedTimeProvider()));
        });
    }
    public async Task Initialize()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClaimsDbContext>();
        await db.Database.EnsureCreatedAsync();
        await DevelopmentSeeder.Seed(db);
    }
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) connection.Dispose();
    }
}

public sealed class FixedTimeProvider : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(2026, 1, 10, 12, 0, 0, TimeSpan.Zero);
}

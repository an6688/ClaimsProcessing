using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Claims.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Claims.IntegrationTests;

public class ProcessingApiTests
{
    private static object Request(int member = 1, int provider = 1, string code = "SYN-01",
        string date = "2025-01-01", decimal price = 12.25m, int quantity = 2) => new
    {
        memberId = $"10000000-0000-0000-0000-{member:000000000000}",
        providerId = $"20000000-0000-0000-0000-{provider:000000000000}",
        lines = new[] { new { procedureCode = code, serviceDate = date, quantity, unitPrice = price } }
    };

    private static async Task<string> Submit(HttpClient client, object request)
    {
        using var response = await client.PostAsJsonAsync("/api/claims", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetString()!;
    }

    private static async Task<JsonElement> Process(HttpClient client, string id)
    {
        using var response = await client.PostAsync($"/api/claims/{id}/process", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task DevelopmentSeedRepairsKnownSyntheticRowsWithoutDeletingClaims()
    {
        using var factory = new ApiFactory();
        await factory.Initialize();
        using var client = factory.CreateClient();
        var claimId = await Submit(client, Request());

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClaimsDbContext>();
            var member = await db.Members.SingleAsync(m => m.MemberNumber == "SYN-M001");
            var provider = await db.Providers.SingleAsync(p => p.ProviderNumber == "SYN-P001");
            member.Name = "Stale Member";
            member.IsActive = false;
            member.CoverageStart = new(2099, 1, 1);
            member.CoverageEnd = new(2099, 1, 1);
            provider.Name = "Stale Provider";
            provider.IsActive = false;
            await db.SaveChangesAsync();

            await DevelopmentSeeder.Seed(db);

            Assert.Equal("Synthetic Member 1", member.Name);
            Assert.True(member.IsActive);
            Assert.Equal(new DateOnly(2020, 1, 1), member.CoverageStart);
            Assert.Null(member.CoverageEnd);
            Assert.Equal("Synthetic Clinic 1", provider.Name);
            Assert.True(provider.IsActive);
            Assert.Equal(3, await db.Members.CountAsync());
            Assert.Equal(3, await db.Providers.CountAsync());
            Assert.Equal(1, await db.Claims.CountAsync());
        }

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/claims/{claimId}")).StatusCode);
    }

    [Theory]
    [InlineData(1, 1, "Approved", null)]
    [InlineData(1, 3, "Denied", "InactiveProvider")]
    [InlineData(2, 1, "Denied", "InactiveCoverage")]
    [InlineData(3, 1, "Denied", "InactiveCoverage")]
    public async Task DecisionsPersistAndCanBeFiltered(int member, int provider, string status, string? denialCode)
    {
        using var factory = new ApiFactory();
        await factory.Initialize();
        using var client = factory.CreateClient();
        var id = await Submit(client, Request(member, provider));

        var processed = await Process(client, id);
        Assert.Equal(status, processed.GetProperty("status").GetString());
        Assert.NotEqual(JsonValueKind.Null, processed.GetProperty("validatedAt").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, processed.GetProperty("processedAt").ValueKind);
        if (denialCode is null)
            Assert.Equal(JsonValueKind.Null, processed.GetProperty("denialReason").ValueKind);
        else
        {
            var reason = processed.GetProperty("denialReason");
            Assert.Equal(denialCode, reason.GetProperty("code").GetString());
            Assert.False(string.IsNullOrWhiteSpace(reason.GetProperty("message").GetString()));
        }

        var retrieved = await client.GetFromJsonAsync<JsonElement>($"/api/claims/{id}");
        Assert.Equal(processed.GetRawText(), retrieved.GetRawText());
        var repeated = await Process(client, id);
        Assert.Equal(processed.GetRawText(), repeated.GetRawText());
        var page = await client.GetFromJsonAsync<JsonElement>($"/api/claims?status={status}");
        Assert.Single(page.GetProperty("items").EnumerateArray());
        Assert.Equal(id, page.GetProperty("items")[0].GetProperty("id").GetString());
        var submitted = await client.GetFromJsonAsync<JsonElement>("/api/claims?status=Submitted");
        Assert.Empty(submitted.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task EquivalentServiceOnProcessedClaimIsDeniedAsDuplicate()
    {
        using var factory = new ApiFactory();
        await factory.Initialize();
        using var client = factory.CreateClient();
        var original = await Submit(client, Request(code: "Syn-exam"));
        var pending = await Submit(client, Request(code: "SYN-EXAM", quantity: 3, price: 99));
        Assert.Equal("Approved", (await Process(client, original)).GetProperty("status").GetString());

        var duplicate = await Process(client, pending);

        Assert.Equal("Denied", duplicate.GetProperty("status").GetString());
        Assert.Equal("DuplicateClaim", duplicate.GetProperty("denialReason").GetProperty("code").GetString());
        Assert.Equal(original, duplicate.GetProperty("denialReason").GetProperty("duplicateClaimId").GetString());
        var read = await client.GetFromJsonAsync<JsonElement>($"/api/claims/{pending}");
        Assert.Equal(duplicate.GetRawText(), read.GetRawText());
    }

    [Fact]
    public async Task PreviouslyDeniedClaimAlsoCountsAsProcessed()
    {
        using var factory = new ApiFactory();
        await factory.Initialize();
        using var client = factory.CreateClient();
        var original = await Submit(client, Request(provider: 3));
        Assert.Equal("InactiveProvider", (await Process(client, original)).GetProperty("denialReason").GetProperty("code").GetString());
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClaimsDbContext>();
            var provider = await db.Providers.SingleAsync(p => p.ProviderNumber == "SYN-P003");
            provider.IsActive = true;
            await db.SaveChangesAsync();
        }
        var second = await Submit(client, Request(provider: 3));
        Assert.Equal("DuplicateClaim", (await Process(client, second)).GetProperty("denialReason").GetProperty("code").GetString());
        // An existing denial is not re-evaluated even if eligibility subsequently changes.
        Assert.Equal("InactiveProvider", (await Process(client, original)).GetProperty("denialReason").GetProperty("code").GetString());
    }

    [Theory]
    [InlineData(2, 1, "SYN-01", "2024-01-01")]
    [InlineData(1, 2, "SYN-01", "2024-01-01")]
    [InlineData(1, 1, "SYN-02", "2024-01-01")]
    [InlineData(1, 1, "SYN-01", "2024-01-02")]
    public async Task DifferentBusinessIdentityIsNotDuplicate(int member, int provider, string code, string date)
    {
        using var factory = new ApiFactory();
        await factory.Initialize();
        using var client = factory.CreateClient();
        var original = await Submit(client, Request(date: "2024-01-01"));
        await Process(client, original);
        var other = await Submit(client, Request(member, provider, code, date));
        Assert.Equal("Approved", (await Process(client, other)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task DuplicateMatchingUsesDateAndCodeTogetherAcrossMultipleLines()
    {
        using var factory = new ApiFactory();
        await factory.Initialize();
        using var client = factory.CreateClient();
        var original = await Submit(client, new
        {
            memberId = "10000000-0000-0000-0000-000000000001",
            providerId = "20000000-0000-0000-0000-000000000001",
            lines = new[]
            {
                new { procedureCode = "A", serviceDate = "2025-01-01", quantity = 1, unitPrice = 10 },
                new { procedureCode = "B", serviceDate = "2025-01-02", quantity = 1, unitPrice = 10 }
            }
        });
        await Process(client, original);
        var differentPair = await Submit(client, Request(code: "B", date: "2025-01-01"));
        Assert.Equal("Approved", (await Process(client, differentPair)).GetProperty("status").GetString());
        var samePair = await Submit(client, Request(code: "B", date: "2025-01-02"));
        var denied = await Process(client, samePair);
        Assert.Equal("DuplicateClaim", denied.GetProperty("denialReason").GetProperty("code").GetString());
        Assert.Equal(original, denied.GetProperty("denialReason").GetProperty("duplicateClaimId").GetString());
    }

    [Theory]
    [InlineData("2099-01-01", 1, "SYN-01")]
    [InlineData("2025-01-01", 0, "SYN-01")]
    [InlineData("2025-01-01", -1, "SYN-01")]
    [InlineData("2025-01-01", 1.001, "SYN-01")]
    [InlineData("2025-01-01", 1, "invalid code")]
    public async Task InvalidInputIsRejectedWithoutPersisting(string date, decimal price, string code)
    {
        using var factory = new ApiFactory();
        await factory.Initialize();
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/claims", Request(code: code, date: date, price: price));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Invalid request", error.GetProperty("title").GetString());
        Assert.Equal(400, error.GetProperty("status").GetInt32());
        Assert.Equal(JsonValueKind.Object, error.GetProperty("errors").ValueKind);
        Assert.NotEmpty(error.GetProperty("errors").EnumerateObject());
        Assert.True(error.TryGetProperty("traceId", out _));
        var claims = await client.GetFromJsonAsync<JsonElement>("/api/claims");
        Assert.Equal(0, claims.GetProperty("totalCount").GetInt32());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("{")]
    [InlineData("{\"memberId\":\"10000000-0000-0000-0000-000000000001\",\"providerId\":\"20000000-0000-0000-0000-000000000001\",\"lines\":[null]}")]
    public async Task BindingAndServiceErrorsUseTheSameValidationStructure(string json)
    {
        using var factory = new ApiFactory();
        await factory.Initialize();
        using var client = factory.CreateClient();
        using var response = await client.PostAsync("/api/claims", new StringContent(json, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Invalid request", error.GetProperty("title").GetString());
        Assert.Equal(400, error.GetProperty("status").GetInt32());
        Assert.Equal(JsonValueKind.Object, error.GetProperty("errors").ValueKind);
        Assert.True(error.TryGetProperty("traceId", out _));
    }

    [Fact]
    public async Task MissingProcessTargetAndUnknownProviderReturn404()
    {
        using var factory = new ApiFactory();
        await factory.Initialize();
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/api/claims/{Guid.NewGuid()}/process", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/api/claims", Request(provider: 99))).StatusCode);
    }

    [Theory]
    [InlineData(100, HttpStatusCode.Created)]
    [InlineData(101, HttpStatusCode.BadRequest)]
    public async Task LineCountBoundaryIsEnforced(int count, HttpStatusCode expected)
    {
        using var factory = new ApiFactory();
        await factory.Initialize();
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/claims", new
        {
            memberId = "10000000-0000-0000-0000-000000000001",
            providerId = "20000000-0000-0000-0000-000000000001",
            lines = Enumerable.Range(1, count).Select(i => new
            {
                procedureCode = $"SYN-{i}", serviceDate = "2025-01-01", quantity = 1, unitPrice = 1
            })
        });
        Assert.Equal(expected, response.StatusCode);
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Claims.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace Claims.IntegrationTests;

public class ApiTests
{
    [Fact]
    public async Task DevelopmentOpenApiDescribesAllEndpoints()
    {
        using var factory = new ApiFactory("Development");
        await factory.Initialize();
        using var client = factory.CreateClient();
        var document = await client.GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json");
        var paths = document.GetProperty("paths");
        Assert.True(paths.GetProperty("/api/claims").TryGetProperty("post", out _));
        Assert.True(paths.GetProperty("/api/claims").TryGetProperty("get", out _));
        Assert.True(paths.TryGetProperty("/api/claims/{id}", out _));
        Assert.True(paths.TryGetProperty("/api/members/{id}/claims", out _));
        Assert.True(paths.TryGetProperty("/api/health", out _));
        Assert.True(paths.GetProperty("/api/claims/{id}/process").TryGetProperty("post", out _));
        Assert.True(paths.GetProperty("/api/health").GetProperty("get").GetProperty("responses").TryGetProperty("503", out _));
        var postResponses = paths.GetProperty("/api/claims").GetProperty("post").GetProperty("responses");
        foreach (var status in new[] { "400", "404", "413", "415", "500" })
            Assert.True(postResponses.GetProperty(status).GetProperty("content").TryGetProperty("application/problem+json", out _));
        var schemas = document.GetProperty("components").GetProperty("schemas");
        var requiredClaim = schemas.GetProperty("SubmitClaimRequest").GetProperty("required").EnumerateArray()
            .Select(field => field.GetString()).ToArray();
        foreach (var field in new[] { "memberId", "providerId", "lines" })
            Assert.Contains(field, requiredClaim);
        var requiredLine = schemas.GetProperty("ClaimLineRequest").GetProperty("required").EnumerateArray()
            .Select(field => field.GetString()).ToArray();
        foreach (var field in new[] { "procedureCode", "serviceDate", "quantity", "unitPrice" })
            Assert.Contains(field, requiredLine);
    }

    private static object Request(Guid? member = null, decimal price = 12.25m) => new {
        memberId = member ?? Guid.Parse("10000000-0000-0000-0000-000000000001"),
        providerId = "20000000-0000-0000-0000-000000000001",
        lines = new[] { new { procedureCode = "SYN-01", serviceDate = "2025-01-01", quantity = 2, unitPrice = price } }
    };
    [Fact]
    public async Task SubmitThenReadReturnsPersistedLinesAndLocation()
    {
        using var factory = new ApiFactory(); await factory.Initialize();
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/claims", Request());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Submitted", body.GetProperty("status").GetString());
        Assert.Equal(24.50m, body.GetProperty("totalAmount").GetDecimal());
        var read = await client.GetAsync(response.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var fetched = await read.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(body.GetProperty("id").GetString(), fetched.GetProperty("id").GetString());
        Assert.Single(fetched.GetProperty("lines").EnumerateArray());
        var line = fetched.GetProperty("lines")[0];
        Assert.Equal(body.GetProperty("lines")[0].GetProperty("id").GetString(), line.GetProperty("id").GetString());
        Assert.Equal("SYN-01", line.GetProperty("procedureCode").GetString());
        Assert.Equal("2025-01-01", line.GetProperty("serviceDate").GetString());
        Assert.Equal(2, line.GetProperty("quantity").GetInt32());
        Assert.Equal(12.25m, line.GetProperty("unitPrice").GetDecimal());
        Assert.Equal(24.50m, line.GetProperty("amount").GetDecimal());
        Assert.Equal(24.50m, fetched.GetProperty("totalAmount").GetDecimal());
    }
    [Theory]
    [InlineData("/api/claims?status=banana")]
    [InlineData("/api/claims?status=999")]
    [InlineData("/api/claims?pageSize=101")]
    [InlineData("/api/claims?page=0")]
    public async Task InvalidQueriesReturn400(string url)
    {
        using var factory = new ApiFactory(); await factory.Initialize();
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(url)).StatusCode);
    }
    [Fact]
    public async Task InvalidRequestsDoNotPersistClaims()
    {
        using var factory = new ApiFactory(); await factory.Initialize();
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/claims", Request(price: 1.001m))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/api/claims", Request(Guid.NewGuid()))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/claims", new { lines = Array.Empty<object>() })).StatusCode);
        using var scope = factory.Services.CreateScope();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<ClaimsDbContext>().Claims.CountAsync());
    }
    [Fact]
    public async Task ListsFilterByMemberAndStatusAndPaginate()
    {
        using var factory = new ApiFactory(); await factory.Initialize();
        using var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/claims", Request());
        await client.PostAsJsonAsync("/api/claims", Request(Guid.Parse("10000000-0000-0000-0000-000000000002")));
        var page = await client.GetFromJsonAsync<JsonElement>("/api/claims?status=Submitted&pageSize=1");
        Assert.Equal(2, page.GetProperty("totalCount").GetInt32());
        Assert.Single(page.GetProperty("items").EnumerateArray());
        var second = await client.GetFromJsonAsync<JsonElement>("/api/claims?status=Submitted&pageSize=1&page=2");
        Assert.Equal(2, second.GetProperty("pageNumber").GetInt32());
        Assert.Equal(2, second.GetProperty("totalCount").GetInt32());
        Assert.Single(second.GetProperty("items").EnumerateArray());
        Assert.NotEqual(page.GetProperty("items")[0].GetProperty("id").GetString(),
            second.GetProperty("items")[0].GetProperty("id").GetString());
        var all = await client.GetFromJsonAsync<JsonElement>("/api/claims?status=Submitted");
        Assert.Equal(all.GetProperty("items")[0].GetProperty("id").GetString(),
            page.GetProperty("items")[0].GetProperty("id").GetString());
        Assert.Equal(all.GetProperty("items")[1].GetProperty("id").GetString(),
            second.GetProperty("items")[0].GetProperty("id").GetString());
        var third = await client.GetFromJsonAsync<JsonElement>("/api/claims?pageSize=1&page=3");
        Assert.Empty(third.GetProperty("items").EnumerateArray());
        var member = await client.GetFromJsonAsync<JsonElement>("/api/members/10000000-0000-0000-0000-000000000001/claims");
        Assert.Equal(1, member.GetProperty("totalCount").GetInt32());
        var approved = await client.GetFromJsonAsync<JsonElement>("/api/claims?status=Approved");
        Assert.Empty(approved.GetProperty("items").EnumerateArray());
    }
    [Fact]
    public async Task MissingClaimAndMemberReturn404()
    {
        using var factory = new ApiFactory(); await factory.Initialize();
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/claims/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/members/{Guid.NewGuid()}/claims")).StatusCode);
    }
    [Fact]
    public async Task DatabaseFailureReturnsSafeProblemAndUnhealthyStatus()
    {
        using var factory = new ApiFactory(); await factory.Initialize();
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/health")).StatusCode);
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ClaimsDbContext>().Database.ExecuteSqlRawAsync("DROP TABLE Members");
        var response = await client.PostAsJsonAsync("/api/claims", Request());
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("traceId", body);
        Assert.DoesNotContain("Sqlite", body);
        Assert.DoesNotContain("stack", body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/api/health")).StatusCode);
    }
}

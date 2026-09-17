using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Claims.IntegrationTests;

public class PortalTests
{
    [Fact]
    public async Task PortalShowsClaimsDashboardAndSubmissionForm()
    {
        using var factory = new ApiFactory();
        await factory.Initialize();
        using var client = factory.CreateClient();

        using var dashboard = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, dashboard.StatusCode);
        var dashboardHtml = await dashboard.Content.ReadAsStringAsync();
        Assert.Contains("Claims dashboard", dashboardHtml);
        Assert.Contains("Submit a claim", dashboardHtml);
        Assert.Contains("/swagger", dashboardHtml);

        using var form = await client.GetAsync("/claims/new");
        Assert.Equal(HttpStatusCode.OK, form.StatusCode);
        var formHtml = await form.Content.ReadAsStringAsync();
        Assert.Contains("Submit a test claim", formHtml);
        Assert.Contains("Synthetic Member 1", formHtml);
        Assert.Contains("Synthetic Clinic 3", formHtml);
    }

    [Fact]
    public async Task PortalDetailShowsStructuredDenial()
    {
        using var factory = new ApiFactory();
        await factory.Initialize();
        using var client = factory.CreateClient();
        using var submitted = await client.PostAsJsonAsync("/api/claims", new
        {
            memberId = "10000000-0000-0000-0000-000000000001",
            providerId = "20000000-0000-0000-0000-000000000003",
            lines = new[]
            {
                new { procedureCode = "PORTAL-TEST", serviceDate = "2025-01-15", quantity = 1, unitPrice = 25m }
            }
        });
        Assert.Equal(HttpStatusCode.Created, submitted.StatusCode);
        var claim = await submitted.Content.ReadFromJsonAsync<JsonElement>();
        var id = claim.GetProperty("id").GetGuid();
        using var process = await client.PostAsync($"/api/claims/{id}/process", null);
        Assert.Equal(HttpStatusCode.OK, process.StatusCode);

        using var detail = await client.GetAsync($"/claims/{id}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        var html = await detail.Content.ReadAsStringAsync();
        Assert.Contains("Claim detail", html);
        Assert.Contains("Inactive Provider", html);
        Assert.Contains("Provider is inactive.", html);
        Assert.Contains("Denied", html);
        Assert.Contains("$25.00", html);
    }
}


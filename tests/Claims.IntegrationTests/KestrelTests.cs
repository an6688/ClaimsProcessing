using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Claims.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Claims.IntegrationTests;

public class KestrelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OversizedBodyReturns413ThroughRealKestrel(bool chunked)
    {
        using var factory = new ApiFactory();
        factory.UseKestrel(0); // Real TCP listener on an available port, not TestServer.
        await factory.Initialize();
        using var client = factory.CreateClient();
        const string validJson = """
            {"memberId":"10000000-0000-0000-0000-000000000001",
             "providerId":"20000000-0000-0000-0000-000000000001",
             "lines":[{"procedureCode":"KST-01","serviceDate":"2025-01-01","quantity":1,"unitPrice":1}]}
            """;
        var oversized = new string(' ', 128 * 1024 + 1) + validJson;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/claims")
        {
            Content = new StringContent(oversized, Encoding.UTF8, "application/json"),
            Version = HttpVersion.Version11
        };
        if (chunked) request.Headers.TransferEncodingChunked = true;
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(413, body.GetProperty("status").GetInt32());
        Assert.Equal("Payload Too Large", body.GetProperty("title").GetString());
        Assert.True(body.TryGetProperty("traceId", out _));
        using var scope = factory.Services.CreateScope();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<ClaimsDbContext>().Claims.CountAsync());
        // The server remains usable, and a normal-sized request still works.
        using var valid = await client.PostAsync("/api/claims", new StringContent(validJson, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Created, valid.StatusCode);
    }
}


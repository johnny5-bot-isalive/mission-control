using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace backend.tests;

public class StatusEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public StatusEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetStatus_ReturnsExpectedPayload()
    {
        using var client = _factory.CreateClient();

        var payload = await client.GetFromJsonAsync<ApiStatusResponse>("/api/status");

        Assert.NotNull(payload);
        Assert.Equal("ASP.NET Core API is running.", payload!.Message);
        Assert.NotEmpty(payload.Environment);
        Assert.StartsWith(".NET", payload.Framework);
    }
}

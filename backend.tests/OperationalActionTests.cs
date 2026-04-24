using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace backend.tests;

public class OperationalActionTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public OperationalActionTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task RunProjectChecks_ExecutesConfiguredRepoChecks()
    {
        using var scope = new MissionControlTestScope();
        scope.SeedRepo("Repo Checks Validation");

        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/actions/run-project-checks", new RunProjectChecksRequest("Repo Checks Validation"));
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<RunProjectChecksResponse>();
        Assert.NotNull(payload);
        Assert.Equal(2, payload!.Commands.Count);
        Assert.All(payload.Commands, command => Assert.True(command.Success));
        Assert.Equal(["npm test", "npm run build"], payload.Commands.Select(command => command.Command).ToArray());
    }

    [Fact]
    public async Task PreviewHelperDispatch_ReturnsPlan_ForSupportedHelper()
    {
        using var scope = new MissionControlTestScope();
        scope.SeedRepo("Helper Preview Validation");

        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/actions/helper-dispatch/preview", new DispatchHelperPreviewRequest("Helper Preview Validation", "review-pass", "Look for sharp edges."));
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<DispatchHelperPreviewResponse>();
        Assert.NotNull(payload);
        Assert.Equal("codex", payload!.Agent);
        Assert.Equal("review-pass", payload.HelperType);
        Assert.Contains(payload.PlannedSteps, step => step.Contains("Inspect", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DispatchHelper_ExecutesThroughCodexShim()
    {
        using var scope = new MissionControlTestScope();
        scope.SeedRepo("Helper Execute Validation");
        scope.InstallFakeCodex(finalMessage: "HELPER_OK", output: "shimmed codex output");

        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/actions/helper-dispatch", new DispatchHelperPreviewRequest("Helper Execute Validation", "review-pass", "Stay narrow."));
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<DispatchHelperRunResponse>();
        Assert.NotNull(payload);
        Assert.True(payload!.Success);
        Assert.Equal(0, payload.ExitCode);
        Assert.Equal("HELPER_OK", payload.FinalMessage);
        Assert.Contains("shimmed codex output", payload.Output);
    }

    [Fact]
    public async Task PreviewHelperDispatch_RejectsUnsupportedHelperType()
    {
        using var scope = new MissionControlTestScope();
        scope.SeedRepo("Unsupported Helper Validation");

        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/actions/helper-dispatch/preview", new DispatchHelperPreviewRequest("Unsupported Helper Validation", "nonsense", null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var message = await response.Content.ReadAsStringAsync();
        Assert.Contains("Unsupported helper type", message, StringComparison.OrdinalIgnoreCase);
    }
}

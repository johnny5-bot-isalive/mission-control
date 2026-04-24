using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace backend.tests;

public class ActivateSprintActionTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ActivateSprintActionTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PreviewActivateSprint_ShowsEligibleCards_AndRegistryUpdate()
    {
        using var scope = new MissionControlTestScope();
        var project = scope.SeedProject(
            "Sprint Validation",
            cadence: "manual",
            backlogBody: """
### SV-001, Eligible card
- Owner: Johnny 5
- Priority: P1
- Next action: Ship it.

### SV-002, Trigger gated
- Owner: Johnny 5
- Priority: P2
- Trigger: waiting on approval
- Next action: Wait.

### SV-003, Future dated
- Owner: Johnny 5
- Priority: P3
- Not before: 2999-01-01
- Next action: Later.
"""
        );
        scope.WriteRegistry([project]);

        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/actions/activate-sprint/preview", new ActivateSprintRequest("Sprint Validation", true));
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<ActivateSprintPreviewResponse>();
        Assert.NotNull(payload);
        Assert.Equal("Sprint Validation", payload!.ProjectName);
        Assert.True(payload.WillUpdateCadence);
        Assert.Equal(3, payload.Cards.Count);
        Assert.Contains(payload.Cards, card => card.Id == "SV-001" && card.Eligible && card.Lane == "Ready");
        Assert.Contains(payload.Cards, card => card.Id == "SV-002" && !card.Eligible);
        Assert.Contains(payload.Cards, card => card.Id == "SV-003" && !card.Eligible);
        Assert.Contains(payload.PlannedWrites, write => write.Path == scope.RegistryPath);
    }

    [Fact]
    public async Task ActivateSprint_MovesEligibleCards_AndUpdatesRegistryCadence()
    {
        using var scope = new MissionControlTestScope();
        var project = scope.SeedProject(
            "Sprint Execute Validation",
            cadence: "manual",
            backlogBody: """
### SE-001, Eligible card
- Owner: Johnny 5
- Priority: P1
- Next action: Move me.

### SE-002, Trigger gated
- Owner: Johnny 5
- Priority: P2
- Trigger: waiting on approval
- Next action: Stay put.
"""
        );
        scope.WriteRegistry([project]);

        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/actions/activate-sprint", new ActivateSprintRequest("Sprint Execute Validation", true));
        response.EnsureSuccessStatusCode();

        var backlogPath = Path.Combine(scope.VaultRoot, project.BacklogRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var kanbanPath = Path.Combine(scope.VaultRoot, project.KanbanRelativePath.Replace('/', Path.DirectorySeparatorChar));

        var backlogContent = await File.ReadAllTextAsync(backlogPath);
        var kanbanContent = await File.ReadAllTextAsync(kanbanPath);
        var registryContent = await File.ReadAllTextAsync(scope.RegistryPath);

        Assert.DoesNotContain("SE-001", backlogContent);
        Assert.Contains("SE-002", backlogContent);
        Assert.Contains("SE-001", kanbanContent);
        Assert.Contains("every-heartbeat", registryContent);
    }
}

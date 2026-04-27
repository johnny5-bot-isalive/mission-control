using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace backend.tests;

public class ProjectsEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ProjectsEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetProjects_ReturnsActiveAndInactiveProjects_ButExcludesArchived()
    {
        using var scope = new MissionControlTestScope();
        var active = scope.SeedProject("Active Project", status: "active", cadence: "every-heartbeat", summary: "Active summary.", includeOptionalNotes: true);
        var inactive = scope.SeedProject("Inactive Project", status: "inactive", cadence: "manual", summary: "Inactive summary.");
        var archived = scope.SeedProject("Archived Project", status: "archived", cadence: "manual", summary: "Archived summary.");
        scope.WriteRegistry([active, inactive, archived]);

        using var client = _factory.CreateClient();
        var projects = await client.GetFromJsonAsync<List<ProjectSummaryResponse>>("/api/projects");

        Assert.NotNull(projects);
        Assert.Equal(2, projects!.Count);
        Assert.Contains(projects, project => project.Name == "Active Project" && project.Status == "active");
        Assert.Contains(projects, project => project.Name == "Inactive Project" && project.Status == "inactive");
        Assert.DoesNotContain(projects, project => project.Name == "Archived Project");

        var activeProject = projects.Single(project => project.Name == "Active Project");
        Assert.Equal("Active summary.", activeProject.Summary);
        Assert.Contains(activeProject.Links, link => link.Label == "Brief");
        Assert.Contains(activeProject.Links, link => link.Label == "Kanban");
        Assert.Contains(activeProject.Links, link => link.Label == "Backlog");
        Assert.Contains(activeProject.Links, link => link.Label == "PRD");

        var backlogLink = activeProject.Links.Single(link => link.Label == "Backlog");
        Assert.Equal(
            "obsidian://open?vault=The%20Nexus&file=40%20Agent%20Nexus%2FProjects%2FActive%20Project%2FProject%20Backlog.md",
            backlogLink.Url);
    }
}

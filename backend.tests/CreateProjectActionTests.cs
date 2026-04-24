using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace backend.tests;

public class CreateProjectActionTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public CreateProjectActionTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PreviewCreateProject_ReturnsPlannedWrites_ForNewProject()
    {
        using var scope = new MissionControlTestScope();
        scope.WriteRegistry([]);

        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/actions/create-project/preview", new CreateProjectRequest("Create Preview Validation", "Short summary"));
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<CreateProjectPreviewResponse>();

        Assert.NotNull(payload);
        Assert.Equal("Create Preview Validation", payload!.ProjectName);
        Assert.Contains("Create Preview Validation", payload.RegistryRow);
        Assert.Equal(5, payload.PlannedWrites.Count);
        Assert.All(payload.PlannedWrites, write => Assert.False(write.Exists));
    }

    [Fact]
    public async Task CreateProject_CreatesExpectedFiles_AndAppendsRegistryRow()
    {
        using var scope = new MissionControlTestScope();
        scope.WriteRegistry([]);

        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/actions/create-project", new CreateProjectRequest("Create Execute Validation", "Created from test"));
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<CreateProjectPreviewResponse>();
        Assert.NotNull(payload);

        Assert.True(Directory.Exists(payload!.FolderPath));
        Assert.True(File.Exists(Path.Combine(payload.FolderPath, "Project Brief.md")));
        Assert.True(File.Exists(Path.Combine(payload.FolderPath, "Project Backlog.md")));
        Assert.True(File.Exists(Path.Combine(payload.FolderPath, "Project Kanban.md")));
        Assert.True(File.Exists(Path.Combine(payload.FolderPath, "Operating Notes.md")));

        var registryContent = await File.ReadAllTextAsync(scope.RegistryPath);
        Assert.Contains("Create Execute Validation", registryContent);

        var projects = await client.GetFromJsonAsync<List<ProjectSummaryResponse>>("/api/projects");
        Assert.Contains(projects!, project => project.Name == "Create Execute Validation");
    }

    [Fact]
    public async Task PreviewCreateProject_RejectsDuplicateRegistryRow()
    {
        using var scope = new MissionControlTestScope();
        var existing = scope.SeedProject("Duplicate Project", summary: "Already there.");
        scope.WriteRegistry([existing]);

        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/actions/create-project/preview", new CreateProjectRequest("Duplicate Project", "Should fail"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var message = await response.Content.ReadAsStringAsync();
        Assert.Contains("already exists", message, StringComparison.OrdinalIgnoreCase);
    }
}

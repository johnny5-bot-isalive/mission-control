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
    public async Task PreviewCreateProject_ForResearch_IncludesResearchLandingZoneWrites()
    {
        using var scope = new MissionControlTestScope();
        scope.WriteRegistry([]);

        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/actions/create-project/preview",
            new CreateProjectRequest("Research Preview Validation", "Research summary", "Research"));
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<CreateProjectPreviewResponse>();

        Assert.NotNull(payload);
        Assert.Equal("Research Preview Validation", payload!.ProjectName);
        Assert.Equal(10, payload.PlannedWrites.Count);
        Assert.Contains(payload.PlannedWrites, write => write.Path.EndsWith("20 Library/Research Reports/Research Preview Validation", StringComparison.Ordinal));
        Assert.Contains(payload.PlannedWrites, write => write.Path.EndsWith("20 Library/Research Reports/Research Preview Validation/Research Brief.md", StringComparison.Ordinal));
        Assert.Contains(payload.PlannedWrites, write => write.Path.EndsWith("20 Library/Research Reports/Research Preview Validation/Process Log.md", StringComparison.Ordinal));
        Assert.Contains(payload.PlannedWrites, write => write.Path.EndsWith("20 Library/Research Reports/Research Preview Validation/Sources", StringComparison.Ordinal));
        Assert.Contains(payload.PlannedWrites, write => write.Path.EndsWith("20 Library/Research Reports/Research Preview Validation/Research Runs", StringComparison.Ordinal));
        Assert.All(payload.PlannedWrites, write => Assert.False(write.Exists));
    }

    [Fact]
    public async Task CreateProject_ForResearch_CreatesProjectAndResearchScaffold()
    {
        using var scope = new MissionControlTestScope();
        scope.WriteRegistry([]);

        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/actions/create-project",
            new CreateProjectRequest("Research Execute Validation", "Research project", "Research"));
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<CreateProjectPreviewResponse>();
        Assert.NotNull(payload);

        Assert.True(Directory.Exists(payload!.FolderPath));
        Assert.True(File.Exists(Path.Combine(payload.FolderPath, "Project Brief.md")));

        var researchHome = Path.Combine(scope.VaultRoot, "20 Library", "Research Reports", "Research Execute Validation");
        Assert.True(Directory.Exists(researchHome));
        Assert.True(File.Exists(Path.Combine(researchHome, "Research Brief.md")));
        Assert.True(File.Exists(Path.Combine(researchHome, "Process Log.md")));
        Assert.True(Directory.Exists(Path.Combine(researchHome, "Sources")));
        Assert.True(Directory.Exists(Path.Combine(researchHome, "Research Runs")));

        var researchBrief = await File.ReadAllTextAsync(Path.Combine(researchHome, "Research Brief.md"));
        Assert.Contains("Mode 3", researchBrief);
        Assert.Contains("Research Runs/", researchBrief);

        var registryContent = await File.ReadAllTextAsync(scope.RegistryPath);
        Assert.Contains("Research Execute Validation", registryContent);
    }

    [Fact]
    public async Task PreviewCreateProject_RejectsUnsupportedProjectType()
    {
        using var scope = new MissionControlTestScope();
        scope.WriteRegistry([]);

        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/actions/create-project/preview",
            new CreateProjectRequest("Bad Type Validation", "Nope", "Marketing"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var message = await response.Content.ReadAsStringAsync();
        Assert.Contains("Unsupported project type", message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("../Escaped Project")]
    [InlineData("Nested/Project")]
    [InlineData("Nested\\Project")]
    [InlineData("Registry | Breaker")]
    [InlineData("Bad\nName")]
    [InlineData("..")]
    public async Task PreviewCreateProject_RejectsUnsafeProjectNames(string projectName)
    {
        using var scope = new MissionControlTestScope();
        scope.WriteRegistry([]);

        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/actions/create-project/preview",
            new CreateProjectRequest(projectName, "Should fail"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var message = await response.Content.ReadAsStringAsync();
        Assert.Contains("Project name", message, StringComparison.OrdinalIgnoreCase);
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

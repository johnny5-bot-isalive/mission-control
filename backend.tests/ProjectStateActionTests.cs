using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace backend.tests;

public class ProjectStateActionTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ProjectStateActionTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task SetProjectActiveState_DeactivatesProject_AndUpdatesProjectFiles()
    {
        using var scope = new MissionControlTestScope();
        var project = scope.SeedProject("State Toggle Validation", status: "active", cadence: "every-heartbeat");
        scope.WriteRegistry([project]);

        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/actions/set-project-active-state", new SetProjectActiveStateRequest(project.Name, false));
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<SetProjectActiveStateResponse>();
        Assert.NotNull(payload);
        Assert.Equal("inactive", payload!.Status);
        Assert.Equal("manual", payload.Cadence);

        var registryContent = await File.ReadAllTextAsync(scope.RegistryPath);
        Assert.Contains("| State Toggle Validation | inactive | P1 | manual |", registryContent);

        var projectFolder = Path.Combine(scope.VaultRoot, project.FolderRelativePath.Replace('/', Path.DirectorySeparatorChar));

        var briefPath = Path.Combine(projectFolder, "Project Brief.md");
        var briefContent = await File.ReadAllTextAsync(briefPath);
        Assert.Contains("status: \"inactive\"", briefContent);

        var indexPath = Path.Combine(projectFolder, "_index.md");
        var indexContent = await File.ReadAllTextAsync(indexPath);
        Assert.Contains("status: \"inactive\"", indexContent);
    }

    [Fact]
    public async Task ArchiveProject_PreviewsAndMovesProjectIntoArchive()
    {
        using var scope = new MissionControlTestScope();
        var project = scope.SeedProject("Archive Validation", status: "inactive", cadence: "manual");
        scope.WriteRegistry([project], ["Archive Validation is a temporary validation project."]);

        using var client = _factory.CreateClient();
        var previewResponse = await client.PostAsJsonAsync("/api/actions/archive-project/preview", new ArchiveProjectRequest(project.Name));
        previewResponse.EnsureSuccessStatusCode();

        var preview = await previewResponse.Content.ReadFromJsonAsync<ArchiveProjectPreviewResponse>();
        Assert.NotNull(preview);
        Assert.Contains(preview!.PlannedSteps, step => step.Contains("Move the project folder", StringComparison.Ordinal));
        Assert.Single(preview.RegistryNotesToRemove);

        var executeResponse = await client.PostAsJsonAsync("/api/actions/archive-project", new ArchiveProjectRequest(project.Name));
        executeResponse.EnsureSuccessStatusCode();

        var archiveFolder = Path.Combine(scope.VaultRoot, "99 Archive", "Project Archive", project.Name);
        Assert.True(Directory.Exists(archiveFolder));

        var registryContent = await File.ReadAllTextAsync(scope.RegistryPath);
        Assert.DoesNotContain("Archive Validation", registryContent);

        var archivedBrief = await File.ReadAllTextAsync(Path.Combine(archiveFolder, "Project Brief.md"));
        Assert.Contains("status: \"archived\"", archivedBrief);

        var projects = await client.GetFromJsonAsync<List<ProjectSummaryResponse>>("/api/projects");
        Assert.NotNull(projects);
        Assert.Empty(projects!);
    }
}

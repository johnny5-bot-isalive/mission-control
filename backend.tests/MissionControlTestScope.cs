using System.Text;

namespace backend.tests;

internal sealed class MissionControlTestScope : IDisposable
{
    private readonly string? _previousVaultRoot;

    public MissionControlTestScope()
    {
        RootPath = Path.Combine(Path.GetTempPath(), $"mission-control-tests-{Guid.NewGuid():N}");
        VaultRoot = Path.Combine(RootPath, "vault");
        Directory.CreateDirectory(VaultRoot);

        _previousVaultRoot = Environment.GetEnvironmentVariable("MISSION_CONTROL_VAULT_ROOT");

        Environment.SetEnvironmentVariable("MISSION_CONTROL_VAULT_ROOT", VaultRoot);
    }

    public string RootPath { get; }
    public string VaultRoot { get; }
    public string RegistryPath => Path.Combine(VaultRoot, "40 Agent Nexus", "Project Registry.md");
    public string KanbanIndexPath => Path.Combine(VaultRoot, "40 Agent Nexus", "Kanban Index.md");

    public RegistryProject SeedProject(
        string name,
        string status = "active",
        string cadence = "manual",
        string summary = "Validation summary.",
        string backlogBody = "_None._",
        string readyBody = "_None._",
        string doingBody = "_None._",
        string blockedBody = "_None._",
        string doneBody = "_None._",
        bool includeOptionalNotes = false)
    {
        var project = new RegistryProject(name, status, cadence);
        var folder = Path.Combine(VaultRoot, project.FolderRelativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(folder);

        File.WriteAllText(Path.Combine(folder, "_index.md"), $$"""
---
type: folder_index
status: {{status}}
project: "{{name}}"
---
# Folder: {{name}}
""");

        File.WriteAllText(Path.Combine(folder, "Project Brief.md"), $$"""
---
type: "project"
status: "{{status}}"
project: "{{name}}"
---
# Project Brief: {{name}}

## Summary
{{summary}}

## Current status
Working.

## Immediate next move
Keep going.
""");

        File.WriteAllText(Path.Combine(folder, "Project Backlog.md"), $$"""
---
type: "project"
status: "{{status}}"
project: "{{name}}"
---
# Project Backlog: {{name}}

## Backlog

{{backlogBody}}
""");

        File.WriteAllText(Path.Combine(folder, "Project Kanban.md"), $$"""
---
type: "project"
status: "{{status}}"
project: "{{name}}"
---
# Project Kanban: {{name}}

## Ready

{{readyBody}}

## Doing

{{doingBody}}

## Blocked

{{blockedBody}}

## Done

{{doneBody}}
""");

        File.WriteAllText(Path.Combine(folder, "Operating Notes.md"), $$"""
---
type: "project-note"
status: "{{status}}"
project: "{{name}}"
---
# Operating Notes: {{name}}

## Operational note
Keep it tidy.
""");

        if (includeOptionalNotes)
        {
            File.WriteAllText(Path.Combine(folder, "Product Requirements Document.md"), $$"""
---
type: "prd"
status: "{{status}}"
project: "{{name}}"
---
# Product Requirements Document: {{name}}
""");
        }

        return project;
    }

    public void WriteRegistry(IEnumerable<RegistryProject> projects, IEnumerable<string>? notes = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(RegistryPath)!);

        var builder = new StringBuilder();
        builder.AppendLine("# Project Registry");
        builder.AppendLine();
        builder.AppendLine("## Active projects");
        builder.AppendLine();
        builder.AppendLine("| Project | Status | Priority | Cadence | Session / thread | Folder path | Backlog path | Kanban path | Last dispatched | Last confirmed progress | Blocker summary | Pilot |");
        builder.AppendLine("| ------- | ------ | -------- | ------- | ---------------- | ----------- | ------------ | ----------- | --------------- | ----------------------- | --------------- | ----- |");

        foreach (var project in projects)
        {
            builder.AppendLine($"| {project.Name} | {project.Status} | P1 | {project.Cadence} | not yet assigned | `{project.FolderRelativePath}` | {BuildRegistryLink(project.BacklogRelativePath)} | {BuildRegistryLink(project.KanbanRelativePath)} | 2026-04-23 | 2026-04-23 | none | no |");
        }

        builder.AppendLine();
        builder.AppendLine("## Notes");

        foreach (var note in notes ?? [])
        {
            builder.AppendLine($"- {note}");
        }

        builder.AppendLine();
        builder.AppendLine("#openclaw #agent-ops #workflow #registry");

        File.WriteAllText(RegistryPath, builder.ToString());
        WriteKanbanIndex(projects);
    }

    private void WriteKanbanIndex(IEnumerable<RegistryProject> projects)
    {
        var projectList = projects.ToList();
        var builder = new StringBuilder();
        builder.AppendLine("---");
        builder.AppendLine("type: moc");
        builder.AppendLine("status: active");
        builder.AppendLine("created: 2026-04-21");
        builder.AppendLine("---");
        builder.AppendLine("# Kanban Index");
        builder.AppendLine();
        builder.AppendLine("## Shared control plane");
        builder.AppendLine("- [[Kanban|Global Kanban]]");
        builder.AppendLine("- [[Backlog|Global Backlog]]");
        builder.AppendLine("- [[Project Registry]]");
        builder.AppendLine();
        builder.AppendLine("## Active project Kanbans");
        foreach (var project in projectList)
        {
            builder.AppendLine($"- [[Projects/{project.Name}/Project Kanban|{project.Name} Kanban]]");
        }
        builder.AppendLine();
        builder.AppendLine("## Project backlogs");
        foreach (var project in projectList)
        {
            builder.AppendLine($"- [[Projects/{project.Name}/Project Backlog|{project.Name} Backlog]]");
        }
        builder.AppendLine();
        builder.AppendLine("## Active board view");
        builder.AppendLine("```dataviewjs");
        builder.AppendLine("const boards = [");
        builder.AppendLine("  { label: \"Global Kanban\", path: \"40 Agent Nexus/Kanban.md\" },");
        foreach (var project in projectList)
        {
            builder.AppendLine($"  {{ label: \"{project.Name}\", path: \"40 Agent Nexus/Projects/{project.Name}/Project Kanban.md\" }},");
        }
        builder.AppendLine("];\n```\n");
        builder.AppendLine("## Notes");
        builder.AppendLine("- Test fixture note.");

        File.WriteAllText(KanbanIndexPath, builder.ToString());
    }

    private static string BuildRegistryLink(string relativePath)
    {
        var encodedTarget = Uri.EscapeDataString(relativePath.Replace('\\', '/').Replace("40 Agent Nexus/", string.Empty))
            .Replace("%2F", "/");

        return $"[`{relativePath}`]({encodedTarget})";
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("MISSION_CONTROL_VAULT_ROOT", _previousVaultRoot);

        try
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
        catch
        {
        }
    }
}

internal sealed record RegistryProject(string Name, string Status, string Cadence)
{
    public string FolderRelativePath => $"40 Agent Nexus/Projects/{Name}/";
    public string BacklogRelativePath => $"40 Agent Nexus/Projects/{Name}/Project Backlog.md";
    public string KanbanRelativePath => $"40 Agent Nexus/Projects/{Name}/Project Kanban.md";
}

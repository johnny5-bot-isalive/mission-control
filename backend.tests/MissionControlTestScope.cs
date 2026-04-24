using System.Text;
using System.Text.RegularExpressions;

namespace backend.tests;

internal sealed class MissionControlTestScope : IDisposable
{
    private readonly string? _previousVaultRoot;
    private readonly string? _previousReposRoot;
    private readonly string? _previousPath;

    public MissionControlTestScope()
    {
        RootPath = Path.Combine(Path.GetTempPath(), $"mission-control-tests-{Guid.NewGuid():N}");
        VaultRoot = Path.Combine(RootPath, "vault");
        ReposRoot = Path.Combine(RootPath, "repos");

        Directory.CreateDirectory(VaultRoot);
        Directory.CreateDirectory(ReposRoot);

        _previousVaultRoot = Environment.GetEnvironmentVariable("MISSION_CONTROL_VAULT_ROOT");
        _previousReposRoot = Environment.GetEnvironmentVariable("MISSION_CONTROL_REPOS_ROOT");
        _previousPath = Environment.GetEnvironmentVariable("PATH");

        Environment.SetEnvironmentVariable("MISSION_CONTROL_VAULT_ROOT", VaultRoot);
        Environment.SetEnvironmentVariable("MISSION_CONTROL_REPOS_ROOT", ReposRoot);
    }

    public string RootPath { get; }
    public string VaultRoot { get; }
    public string ReposRoot { get; }
    public string RegistryPath => Path.Combine(VaultRoot, "40 Agent Nexus", "Project Registry.md");

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
            builder.AppendLine($"| {project.Name} | {project.Status} | P1 | {project.Cadence} | not yet assigned | `{project.FolderRelativePath}` | `{project.BacklogRelativePath}` | `{project.KanbanRelativePath}` | 2026-04-23 | 2026-04-23 | none | no |");
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
    }

    public string SeedRepo(string projectName)
    {
        var slug = Regex.Replace(projectName.Trim().ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
        var repoPath = Path.Combine(ReposRoot, slug);
        Directory.CreateDirectory(repoPath);

        File.WriteAllText(Path.Combine(repoPath, "package.json"), $$"""
{
  "name": "{{slug}}",
  "private": true,
  "scripts": {
    "test": "node -e \"process.exit(0)\"",
    "build": "node -e \"process.exit(0)\""
  }
}
""");

        return repoPath;
    }

    public string InstallFakeCodex(string finalMessage = "FAKE_CODEX_OK", string output = "fake codex output")
    {
        var binPath = Path.Combine(RootPath, "bin");
        Directory.CreateDirectory(binPath);

        var scriptPath = Path.Combine(binPath, "codex");
        File.WriteAllText(scriptPath, $$"""
#!/usr/bin/env bash
set -euo pipefail
out_file=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    -o)
      out_file="$2"
      shift 2
      ;;
    *)
      shift
      ;;
  esac
done
cat >/dev/null
if [[ -n "$out_file" ]]; then
  printf '%s\n' '{{finalMessage}}' > "$out_file"
fi
printf '%s\n' '{{output}}'
""");

        System.Diagnostics.Process.Start("/usr/bin/env", $"chmod +x \"{scriptPath}\"")!.WaitForExit();
        Environment.SetEnvironmentVariable("PATH", $"{binPath}:{_previousPath}");
        return scriptPath;
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("MISSION_CONTROL_VAULT_ROOT", _previousVaultRoot);
        Environment.SetEnvironmentVariable("MISSION_CONTROL_REPOS_ROOT", _previousReposRoot);
        Environment.SetEnvironmentVariable("PATH", _previousPath);

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

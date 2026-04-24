using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;

namespace backend.Controllers;

public sealed record RunProjectChecksRequest(string ProjectName);
public sealed record CreateProjectRequest(string ProjectName, string? Summary);

public sealed record ActionCommandResult(
    string Command,
    int ExitCode,
    long DurationMs,
    string Output,
    bool Success);

public sealed record RunProjectChecksResponse(
    string ProjectName,
    string RepoPath,
    IReadOnlyList<ActionCommandResult> Commands);

public sealed record PlannedWriteResponse(string Path, bool Exists);

public sealed record CreateProjectPreviewResponse(
    string ProjectName,
    string FolderPath,
    string RegistryPath,
    string RegistryRow,
    IReadOnlyList<PlannedWriteResponse> PlannedWrites);

public sealed record ActivateSprintRequest(string ProjectName, bool UpdateCadence = true);

public sealed record ActivateSprintCardPreviewResponse(
    string Id,
    string Title,
    bool Eligible,
    string Reason,
    string Lane);

public sealed record ActivateSprintPreviewResponse(
    string ProjectName,
    string BacklogPath,
    string KanbanPath,
    string? RegistryPath,
    bool WillUpdateCadence,
    string Summary,
    IReadOnlyList<ActivateSprintCardPreviewResponse> Cards,
    IReadOnlyList<PlannedWriteResponse> PlannedWrites);

public sealed record SetProjectActiveStateRequest(string ProjectName, bool IsActive);

public sealed record SetProjectActiveStateResponse(
    string ProjectName,
    string Status,
    string Cadence,
    string RegistryPath,
    string Summary,
    IReadOnlyList<string> UpdatedPaths);

public sealed record ArchiveProjectRequest(string ProjectName);

public sealed record ArchiveProjectPreviewResponse(
    string ProjectName,
    string SourceFolderPath,
    string ArchiveFolderPath,
    string RegistryPath,
    string Summary,
    IReadOnlyList<string> PlannedSteps,
    IReadOnlyList<string> RegistryNotesToRemove);

public sealed record DispatchHelperPreviewRequest(
    string ProjectName,
    string HelperType,
    string? Notes);

public sealed record DispatchHelperPreviewResponse(
    string ProjectName,
    string HelperType,
    string Agent,
    string RepoPath,
    string Summary,
    IReadOnlyList<string> PlannedSteps,
    string? Notes);

public sealed record DispatchHelperRunResponse(
    string ProjectName,
    string HelperType,
    string Agent,
    string RepoPath,
    string Summary,
    IReadOnlyList<string> PlannedSteps,
    string? Notes,
    string SandboxMode,
    long DurationMs,
    int ExitCode,
    bool Success,
    string FinalMessage,
    string Output);

[ApiController]
[Route("api/actions")]
public sealed class ActionsController : ControllerBase
{
    private const string DefaultReposRoot = "/home/jaret/repos";
    private const string DefaultVaultRoot = "/mnt/c/Users/Jaret/Obsidian/The Nexus";
    private const string RegistryRelativePath = "40 Agent Nexus/Project Registry.md";

    private static string ReposRoot =>
        Environment.GetEnvironmentVariable("MISSION_CONTROL_REPOS_ROOT") ?? DefaultReposRoot;

    private static string VaultRoot =>
        Environment.GetEnvironmentVariable("MISSION_CONTROL_VAULT_ROOT") ?? DefaultVaultRoot;

    /// <summary>
    /// Runs the standard validation commands for a project's local repository.
    /// </summary>
    [HttpPost("run-project-checks")]
    public async Task<ActionResult<RunProjectChecksResponse>> RunProjectChecks(
        [FromBody] RunProjectChecksRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ProjectName))
        {
            return BadRequest("Project name is required.");
        }

        var repoPath = ResolveRepoPath(request.ProjectName);
        if (repoPath is null)
        {
            return BadRequest($"No repo found for project '{request.ProjectName}'.");
        }

        var commands = new[]
        {
            "npm test",
            "npm run build",
        };

        var results = new List<ActionCommandResult>();
        foreach (var command in commands)
        {
            var result = await RunCommand(repoPath, command, cancellationToken);
            results.Add(result);

            if (!result.Success)
            {
                break;
            }
        }

        return Ok(new RunProjectChecksResponse(
            ProjectName: request.ProjectName,
            RepoPath: repoPath,
            Commands: results));
    }

    /// <summary>
    /// Previews the markdown files and registry row that would be created for a new project.
    /// </summary>
    [HttpPost("create-project/preview")]
    public ActionResult<CreateProjectPreviewResponse> PreviewCreateProject(
        [FromBody] CreateProjectRequest request)
    {
        var plan = BuildCreateProjectPlan(request.ProjectName, request.Summary);
        if (!plan.Success)
        {
            return BadRequest(plan.ErrorMessage);
        }

        return Ok(plan.Response!);
    }

    /// <summary>
    /// Creates a new project folder, starter markdown files, and registry row using the settled Mission Control contract.
    /// </summary>
    [HttpPost("create-project")]
    public ActionResult<CreateProjectPreviewResponse> CreateProject(
        [FromBody] CreateProjectRequest request)
    {
        var plan = BuildCreateProjectPlan(request.ProjectName, request.Summary);
        if (!plan.Success)
        {
            return BadRequest(plan.ErrorMessage);
        }

        var response = plan.Response!;
        if (response.PlannedWrites.Any(write => write.Exists))
        {
            return BadRequest("One or more target files already exist. Review the preview before trying again.");
        }

        Directory.CreateDirectory(response.FolderPath);

        foreach (var file in plan.FilesToWrite)
        {
            System.IO.File.WriteAllText(file.Path, file.Content);
        }

        AppendRegistryRow(plan.RegistryPath, response.RegistryRow);
        return Ok(response);
    }

    /// <summary>
    /// Previews which backlog cards are eligible to move into the active sprint and which markdown files would change.
    /// </summary>
    [HttpPost("activate-sprint/preview")]
    public ActionResult<ActivateSprintPreviewResponse> PreviewActivateSprint(
        [FromBody] ActivateSprintRequest request)
    {
        var plan = BuildActivateSprintPlan(request.ProjectName, request.UpdateCadence);
        if (!plan.Success)
        {
            return BadRequest(plan.ErrorMessage);
        }

        return Ok(plan.Response!);
    }

    /// <summary>
    /// Moves eligible backlog cards into the sprint-ready lane and applies the related markdown updates.
    /// </summary>
    [HttpPost("activate-sprint")]
    public ActionResult<ActivateSprintPreviewResponse> ActivateSprint(
        [FromBody] ActivateSprintRequest request)
    {
        var plan = BuildActivateSprintPlan(request.ProjectName, request.UpdateCadence);
        if (!plan.Success)
        {
            return BadRequest(plan.ErrorMessage);
        }

        foreach (var file in plan.FilesToWrite)
        {
            System.IO.File.WriteAllText(file.Path, file.Content);
        }

        return Ok(plan.Response!);
    }

    /// <summary>
    /// Toggles a project between active and inactive, updating the registry and project-local status frontmatter.
    /// </summary>
    [HttpPost("set-project-active-state")]
    public ActionResult<SetProjectActiveStateResponse> SetProjectActiveState(
        [FromBody] SetProjectActiveStateRequest request)
    {
        var plan = BuildSetProjectActiveStatePlan(request.ProjectName, request.IsActive);
        if (!plan.Success)
        {
            return BadRequest(plan.ErrorMessage);
        }

        foreach (var file in plan.FilesToWrite)
        {
            System.IO.File.WriteAllText(file.Path, file.Content);
        }

        return Ok(plan.Response!);
    }

    /// <summary>
    /// Previews the registry cleanup and folder move that would archive a project.
    /// </summary>
    [HttpPost("archive-project/preview")]
    public ActionResult<ArchiveProjectPreviewResponse> PreviewArchiveProject(
        [FromBody] ArchiveProjectRequest request)
    {
        var plan = BuildArchiveProjectPlan(request.ProjectName);
        if (!plan.Success)
        {
            return BadRequest(plan.ErrorMessage);
        }

        return Ok(plan.Response!);
    }

    /// <summary>
    /// Archives a project by removing its live registry entry, updating markdown state, and moving the folder into the archive area.
    /// </summary>
    [HttpPost("archive-project")]
    public ActionResult<ArchiveProjectPreviewResponse> ArchiveProject(
        [FromBody] ArchiveProjectRequest request)
    {
        var plan = BuildArchiveProjectPlan(request.ProjectName);
        if (!plan.Success)
        {
            return BadRequest(plan.ErrorMessage);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(plan.Response!.ArchiveFolderPath)!);

        foreach (var file in plan.FilesToWrite)
        {
            System.IO.File.WriteAllText(file.Path, file.Content);
        }

        Directory.Move(plan.SourceFolderPath, plan.Response.ArchiveFolderPath);
        return Ok(plan.Response);
    }

    /// <summary>
    /// Previews a bounded helper run, including the chosen agent, sandbox mode, and planned execution steps.
    /// </summary>
    [HttpPost("helper-dispatch/preview")]
    public ActionResult<DispatchHelperPreviewResponse> PreviewHelperDispatch(
        [FromBody] DispatchHelperPreviewRequest request)
    {
        var plan = BuildHelperDispatchPlan(request.ProjectName, request.HelperType, request.Notes);
        if (!plan.Success)
        {
            return BadRequest(plan.ErrorMessage);
        }

        return Ok(plan.Preview!);
    }

    /// <summary>
    /// Executes a bounded helper run against the local repository and returns the full command result.
    /// </summary>
    [HttpPost("helper-dispatch")]
    public async Task<ActionResult<DispatchHelperRunResponse>> DispatchHelper(
        [FromBody] DispatchHelperPreviewRequest request,
        CancellationToken cancellationToken)
    {
        var plan = BuildHelperDispatchPlan(request.ProjectName, request.HelperType, request.Notes);
        if (!plan.Success)
        {
            return BadRequest(plan.ErrorMessage);
        }

        var result = await RunCodexHelper(plan.Preview!, plan.Prompt, plan.SandboxMode, cancellationToken);
        return Ok(result);
    }

    private static (bool Success, string? ErrorMessage, DispatchHelperPreviewResponse? Preview, string Prompt, string SandboxMode) BuildHelperDispatchPlan(
        string projectName,
        string helperType,
        string? notes)
    {
        if (string.IsNullOrWhiteSpace(projectName))
        {
            return (false, "Project name is required.", null, string.Empty, string.Empty);
        }

        if (string.IsNullOrWhiteSpace(helperType))
        {
            return (false, "Helper type is required.", null, string.Empty, string.Empty);
        }

        var cleanProjectName = projectName.Trim();
        var repoPath = ResolveRepoPath(cleanProjectName);
        if (repoPath is null)
        {
            return (false, $"No repo found for project '{cleanProjectName}'.", null, string.Empty, string.Empty);
        }

        var trimmedNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        var normalizedHelperType = helperType.Trim().ToLowerInvariant();

        var plan = normalizedHelperType switch
        {
            "tests" => (
                Preview: new DispatchHelperPreviewResponse(
                    ProjectName: cleanProjectName,
                    HelperType: "tests",
                    Agent: "codex",
                    RepoPath: repoPath,
                    Summary: "Ask Codex for a bounded repo-check pass focused on test failures, build failures, or obvious fix-forward work.",
                    PlannedSteps: [
                        "Open the project repo in Codex as the bounded working target.",
                        "Run the existing test/build checks and summarize failures or confirm green state.",
                        "If something small and safe is broken, fix it and report the exact change set.",
                    ],
                    Notes: trimmedNotes),
                Prompt: BuildHelperPrompt(cleanProjectName, "tests", trimmedNotes),
                SandboxMode: "workspace-write"),
            "ci-wiring" => (
                Preview: new DispatchHelperPreviewResponse(
                    ProjectName: cleanProjectName,
                    HelperType: "ci-wiring",
                    Agent: "codex",
                    RepoPath: repoPath,
                    Summary: "Ask Codex for a narrow CI/setup pass, limited to scripts, workflows, or verification glue already implied by the repo.",
                    PlannedSteps: [
                        "Inspect the repo's existing scripts and workflow files.",
                        "Wire or repair the smallest missing CI/setup piece needed for repeatable checks.",
                        "Return the exact files changed plus any follow-up risk or approval boundary.",
                    ],
                    Notes: trimmedNotes),
                Prompt: BuildHelperPrompt(cleanProjectName, "ci-wiring", trimmedNotes),
                SandboxMode: "workspace-write"),
            "cleanup" => (
                Preview: new DispatchHelperPreviewResponse(
                    ProjectName: cleanProjectName,
                    HelperType: "cleanup",
                    Agent: "codex",
                    RepoPath: repoPath,
                    Summary: "Ask Codex for low-risk cleanup only, things like dead code removal, naming cleanup, or small readability refactors.",
                    PlannedSteps: [
                        "Inspect the current repo surface for obvious low-risk cleanup candidates.",
                        "Avoid architecture pivots or behavior changes unless they are tiny and clearly justified.",
                        "Return a concise cleanup summary plus exact edited files.",
                    ],
                    Notes: trimmedNotes),
                Prompt: BuildHelperPrompt(cleanProjectName, "cleanup", trimmedNotes),
                SandboxMode: "workspace-write"),
            "review-pass" => (
                Preview: new DispatchHelperPreviewResponse(
                    ProjectName: cleanProjectName,
                    HelperType: "review-pass",
                    Agent: "codex",
                    RepoPath: repoPath,
                    Summary: "Ask Codex for a bounded review pass focused on quality, risk, and next concrete moves without turning it into an open-ended swarm.",
                    PlannedSteps: [
                        "Inspect the current project slice and recent action surfaces.",
                        "Call out the sharpest risks, missing guardrails, or obvious follow-up work.",
                        "Return a short review brief with recommended next steps.",
                    ],
                    Notes: trimmedNotes),
                Prompt: BuildHelperPrompt(cleanProjectName, "review-pass", trimmedNotes),
                SandboxMode: "read-only"),
            _ => default
        };

        return plan.Preview is null
            ? (false, $"Unsupported helper type '{helperType}'.", null, string.Empty, string.Empty)
            : (true, null, plan.Preview, plan.Prompt, plan.SandboxMode);
    }

    private static string BuildHelperPrompt(string projectName, string helperType, string? notes)
    {
        var notesBlock = string.IsNullOrWhiteSpace(notes)
            ? string.Empty
            : $"\nOperator notes:\n{notes.Trim()}\n";

        return helperType switch
        {
            "tests" => $"You are Codex acting as a bounded helper for the project '{projectName}'. Work only inside the current repo. Run the existing test and build checks, summarize failures or confirm green state, and only make the smallest safe fixes if something obvious is broken. Do not broaden scope into architecture changes or unrelated cleanup. Report exactly what you changed, what remains, and any follow-up risk.{notesBlock}",
            "ci-wiring" => $"You are Codex acting as a bounded helper for the project '{projectName}'. Inspect the existing scripts and workflow files, then wire or repair the smallest missing CI or setup piece needed for repeatable checks. Stay narrow, avoid speculative platform expansion, and report exact file changes plus any approval boundary or remaining gap.{notesBlock}",
            "cleanup" => $"You are Codex acting as a bounded helper for the project '{projectName}'. Perform only low-risk cleanup, things like dead code removal, naming cleanup, or tiny readability refactors. Avoid architecture pivots and avoid behavior changes unless they are truly minimal and clearly justified. Report exact files changed and why.{notesBlock}",
            "review-pass" => $"You are Codex acting as a bounded helper for the project '{projectName}'. Do a read-only review pass over the current repo state. Do not modify files. Return a short report covering the sharpest risks, missing guardrails, and the next concrete steps worth taking. Keep it concise and specific.{notesBlock}",
            _ => throw new InvalidOperationException($"Unsupported helper type '{helperType}'.")
        };
    }

    private static async Task<DispatchHelperRunResponse> RunCodexHelper(
        DispatchHelperPreviewResponse preview,
        string prompt,
        string sandboxMode,
        CancellationToken cancellationToken)
    {
        var output = new StringBuilder();
        var finalMessagePath = Path.GetTempFileName();
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "codex",
                    WorkingDirectory = preview.RepoPath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = true,
                    UseShellExecute = false,
                },
            };

            process.StartInfo.ArgumentList.Add("exec");
            process.StartInfo.ArgumentList.Add("--ephemeral");
            process.StartInfo.ArgumentList.Add("--skip-git-repo-check");
            process.StartInfo.ArgumentList.Add("-C");
            process.StartInfo.ArgumentList.Add(preview.RepoPath);
            process.StartInfo.ArgumentList.Add("-o");
            process.StartInfo.ArgumentList.Add(finalMessagePath);

            if (string.Equals(sandboxMode, "read-only", StringComparison.OrdinalIgnoreCase))
            {
                process.StartInfo.ArgumentList.Add("--sandbox");
                process.StartInfo.ArgumentList.Add("read-only");
            }
            else
            {
                process.StartInfo.ArgumentList.Add("--full-auto");
            }

            process.StartInfo.ArgumentList.Add("-");

            process.OutputDataReceived += (_, args) =>
            {
                if (args.Data is not null)
                {
                    output.AppendLine(args.Data);
                }
            };
            process.ErrorDataReceived += (_, args) =>
            {
                if (args.Data is not null)
                {
                    output.AppendLine(args.Data);
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.StandardInput.WriteAsync(prompt);
            process.StandardInput.Close();
            await process.WaitForExitAsync(cancellationToken);
            stopwatch.Stop();

            var finalMessage = System.IO.File.Exists(finalMessagePath)
                ? System.IO.File.ReadAllText(finalMessagePath).Trim()
                : string.Empty;

            return new DispatchHelperRunResponse(
                ProjectName: preview.ProjectName,
                HelperType: preview.HelperType,
                Agent: preview.Agent,
                RepoPath: preview.RepoPath,
                Summary: preview.Summary,
                PlannedSteps: preview.PlannedSteps,
                Notes: preview.Notes,
                SandboxMode: sandboxMode,
                DurationMs: stopwatch.ElapsedMilliseconds,
                ExitCode: process.ExitCode,
                Success: process.ExitCode == 0,
                FinalMessage: string.IsNullOrWhiteSpace(finalMessage) ? "No final message captured." : finalMessage,
                Output: output.ToString().Trim());
        }
        finally
        {
            stopwatch.Stop();
            try
            {
                if (System.IO.File.Exists(finalMessagePath))
                {
                    System.IO.File.Delete(finalMessagePath);
                }
            }
            catch
            {
            }
        }
    }

    private static (bool Success, string? ErrorMessage, CreateProjectPreviewResponse? Response, List<FileWritePlan> FilesToWrite, string RegistryPath) BuildCreateProjectPlan(
        string projectName,
        string? summary)
    {
        if (string.IsNullOrWhiteSpace(projectName))
        {
            return (false, "Project name is required.", null, [], string.Empty);
        }

        var cleanName = projectName.Trim();
        var created = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd");
        var folderRelativePath = $"40 Agent Nexus/Projects/{cleanName}";
        var folderPath = CombineVaultPath(folderRelativePath);
        var registryPath = CombineVaultPath(RegistryRelativePath);

        if (!System.IO.File.Exists(registryPath))
        {
            return (false, $"Registry not found: {registryPath}", null, [], registryPath);
        }

        var registryContent = System.IO.File.ReadAllText(registryPath);
        if (RegistryContainsProject(registryContent, cleanName))
        {
            return (false, $"A registry row for '{cleanName}' already exists.", null, [], registryPath);
        }

        var summaryText = string.IsNullOrWhiteSpace(summary)
            ? "Add a project summary during the first planning pass."
            : summary.Trim();

        var filesToWrite = new List<FileWritePlan>
        {
            new(CombineVaultPath(folderRelativePath, "Project Brief.md"), BuildProjectBrief(cleanName, created, summaryText)),
            new(CombineVaultPath(folderRelativePath, "Project Backlog.md"), BuildProjectBacklog(cleanName, created)),
            new(CombineVaultPath(folderRelativePath, "Project Kanban.md"), BuildProjectKanban(cleanName, created)),
            new(CombineVaultPath(folderRelativePath, "Operating Notes.md"), BuildOperatingNotes(cleanName, created)),
        };

        var plannedWrites = new List<PlannedWriteResponse>
        {
            new(folderPath, Directory.Exists(folderPath)),
        };
        plannedWrites.AddRange(filesToWrite.Select(file => new PlannedWriteResponse(file.Path, System.IO.File.Exists(file.Path))));

        var registryRow = BuildRegistryRow(cleanName, folderRelativePath, created);

        var response = new CreateProjectPreviewResponse(
            ProjectName: cleanName,
            FolderPath: folderPath,
            RegistryPath: registryPath,
            RegistryRow: registryRow,
            PlannedWrites: plannedWrites);

        return (true, null, response, filesToWrite, registryPath);
    }

    private static (bool Success, string? ErrorMessage, ActivateSprintPreviewResponse? Response, List<FileWritePlan> FilesToWrite) BuildActivateSprintPlan(
        string projectName,
        bool updateCadence)
    {
        if (string.IsNullOrWhiteSpace(projectName))
        {
            return (false, "Project name is required.", null, []);
        }

        var cleanName = projectName.Trim();
        var registryPath = CombineVaultPath(RegistryRelativePath);
        if (!System.IO.File.Exists(registryPath))
        {
            return (false, $"Registry not found: {registryPath}", null, []);
        }

        var registryContent = System.IO.File.ReadAllText(registryPath);
        var registryRows = ParseRegistryRows(registryContent.Split('\n'));
        var matchingRows = registryRows
            .Where(row => string.Equals(row.Project, cleanName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matchingRows.Count != 1)
        {
            return (false, $"Expected exactly one registry row for '{cleanName}', found {matchingRows.Count}.", null, []);
        }

        var row = matchingRows[0];
        if (string.IsNullOrWhiteSpace(row.BacklogPath) || string.IsNullOrWhiteSpace(row.KanbanPath))
        {
            return (false, $"Registry row for '{cleanName}' is missing backlog or kanban paths.", null, []);
        }

        var backlogPath = CombineVaultPath(row.BacklogPath);
        var kanbanPath = CombineVaultPath(row.KanbanPath);

        if (!System.IO.File.Exists(backlogPath))
        {
            return (false, $"Backlog not found: {backlogPath}", null, []);
        }

        if (!System.IO.File.Exists(kanbanPath))
        {
            return (false, $"Kanban not found: {kanbanPath}", null, []);
        }

        var backlogContent = System.IO.File.ReadAllText(backlogPath);
        var kanbanContent = System.IO.File.ReadAllText(kanbanPath);

        var backlogBody = ExtractSectionBody(backlogContent, "Backlog");
        if (backlogBody is null)
        {
            return (false, $"Malformed backlog note: missing '## Backlog' in {backlogPath}", null, []);
        }

        var backlogSection = SplitTrailingFooter(backlogBody);

        var readyBody = ExtractSectionBody(kanbanContent, "Ready");
        if (readyBody is null)
        {
            return (false, $"Malformed kanban note: missing '## Ready' in {kanbanPath}", null, []);
        }

        foreach (var requiredHeading in new[] { "Doing", "Blocked", "Done" })
        {
            if (ExtractSectionBody(kanbanContent, requiredHeading) is null)
            {
                return (false, $"Malformed kanban note: missing '## {requiredHeading}' in {kanbanPath}", null, []);
            }
        }

        var backlogCards = ParseCardBlocks(backlogSection.Body);
        var kanbanIds = ParseKanbanCardIds(kanbanContent);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var eligibleCards = new List<CardBlock>();
        var cardPreviews = new List<ActivateSprintCardPreviewResponse>();

        foreach (var card in backlogCards)
        {
            var evaluation = EvaluateBacklogCard(card, today);
            if (evaluation.Eligible && kanbanIds.Contains(card.Id))
            {
                return (false, $"Duplicate Kanban card ID detected: '{card.Id}' already exists in {kanbanPath}.", null, []);
            }

            if (evaluation.Eligible)
            {
                eligibleCards.Add(card);
            }

            cardPreviews.Add(new ActivateSprintCardPreviewResponse(
                Id: card.Id,
                Title: card.Title,
                Eligible: evaluation.Eligible,
                Reason: evaluation.Reason,
                Lane: evaluation.Eligible ? "Ready" : "Backlog"));
        }

        if (eligibleCards.Count == 0)
        {
            var emptyResponse = new ActivateSprintPreviewResponse(
                ProjectName: cleanName,
                BacklogPath: backlogPath,
                KanbanPath: kanbanPath,
                RegistryPath: registryPath,
                WillUpdateCadence: false,
                Summary: "No eligible backlog cards found. No board mutation is needed.",
                Cards: cardPreviews,
                PlannedWrites: []);

            return (true, null, emptyResponse, []);
        }

        var remainingBacklogCards = backlogCards.Where(card => eligibleCards.All(eligible => !string.Equals(eligible.Id, card.Id, StringComparison.OrdinalIgnoreCase))).ToList();
        var updatedBacklogSectionBody = BuildCardSectionBody(remainingBacklogCards);
        if (!string.IsNullOrWhiteSpace(backlogSection.Footer))
        {
            updatedBacklogSectionBody += "\n\n" + backlogSection.Footer;
        }

        var updatedBacklogContent = ReplaceSectionBody(backlogContent, "Backlog", updatedBacklogSectionBody);
        var updatedReadyBody = BuildReadySectionBody(readyBody, eligibleCards);
        var updatedKanbanContent = ReplaceSectionBody(kanbanContent, "Ready", updatedReadyBody);

        var filesToWrite = new List<FileWritePlan>
        {
            new(backlogPath, updatedBacklogContent),
            new(kanbanPath, updatedKanbanContent),
        };

        var plannedWrites = new List<PlannedWriteResponse>
        {
            new(backlogPath, true),
            new(kanbanPath, true),
        };

        var willUpdateCadence = false;
        if (updateCadence && !string.Equals(row.Cadence, "every-heartbeat", StringComparison.OrdinalIgnoreCase))
        {
            var updatedRegistryContent = UpdateRegistryCadence(registryContent, cleanName, "every-heartbeat", out var cadenceChanged);
            if (cadenceChanged)
            {
                filesToWrite.Add(new FileWritePlan(registryPath, updatedRegistryContent));
                plannedWrites.Add(new PlannedWriteResponse(registryPath, true));
                willUpdateCadence = true;
            }
        }

        var summary = eligibleCards.Count == 1
            ? "1 eligible backlog card will move into Kanban Ready."
            : $"{eligibleCards.Count} eligible backlog cards will move into Kanban Ready.";

        if (willUpdateCadence)
        {
            summary += " Registry cadence will also update to every-heartbeat.";
        }

        var response = new ActivateSprintPreviewResponse(
            ProjectName: cleanName,
            BacklogPath: backlogPath,
            KanbanPath: kanbanPath,
            RegistryPath: registryPath,
            WillUpdateCadence: willUpdateCadence,
            Summary: summary,
            Cards: cardPreviews,
            PlannedWrites: plannedWrites);

        return (true, null, response, filesToWrite);
    }

    private static (bool Success, string? ErrorMessage, SetProjectActiveStateResponse? Response, List<FileWritePlan> FilesToWrite) BuildSetProjectActiveStatePlan(
        string projectName,
        bool isActive)
    {
        if (string.IsNullOrWhiteSpace(projectName))
        {
            return (false, "Project name is required.", null, []);
        }

        var cleanName = projectName.Trim();
        var registryPath = CombineVaultPath(RegistryRelativePath);
        if (!System.IO.File.Exists(registryPath))
        {
            return (false, $"Registry not found: {registryPath}", null, []);
        }

        var registryContent = System.IO.File.ReadAllText(registryPath);
        var registryRows = ParseRegistryRows(registryContent.Split('\n'));
        var matchingRows = registryRows
            .Where(row => string.Equals(row.Project, cleanName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matchingRows.Count != 1)
        {
            return (false, $"Expected exactly one registry row for '{cleanName}', found {matchingRows.Count}.", null, []);
        }

        var row = matchingRows[0];
        var nextStatus = isActive ? "active" : "inactive";
        var nextCadence = isActive ? row.Cadence : "manual";
        var updatedRegistryContent = UpdateRegistryRowState(registryContent, cleanName, nextStatus, nextCadence, out var registryChanged);
        if (!registryChanged)
        {
            return (false, $"Registry row for '{cleanName}' could not be updated.", null, []);
        }

        var filesToWrite = new List<FileWritePlan>
        {
            new(registryPath, updatedRegistryContent),
        };

        var updatedPaths = new List<string> { registryPath };
        foreach (var notePath in GetProjectStateNotePaths(row.FolderPath))
        {
            if (!System.IO.File.Exists(notePath))
            {
                continue;
            }

            var existingContent = System.IO.File.ReadAllText(notePath);
            var updatedContent = UpdateFrontmatterStatus(existingContent, nextStatus);
            filesToWrite.Add(new FileWritePlan(notePath, updatedContent));
            updatedPaths.Add(notePath);
        }

        var response = new SetProjectActiveStateResponse(
            ProjectName: cleanName,
            Status: nextStatus,
            Cadence: nextCadence,
            RegistryPath: registryPath,
            Summary: isActive
                ? "Project reactivated. Registry status is active again."
                : "Project deactivated. Registry status is inactive and cadence is now manual.",
            UpdatedPaths: updatedPaths);

        return (true, null, response, filesToWrite);
    }

    private static (bool Success, string? ErrorMessage, ArchiveProjectPreviewResponse? Response, List<FileWritePlan> FilesToWrite, string SourceFolderPath) BuildArchiveProjectPlan(
        string projectName)
    {
        if (string.IsNullOrWhiteSpace(projectName))
        {
            return (false, "Project name is required.", null, [], string.Empty);
        }

        var cleanName = projectName.Trim();
        var registryPath = CombineVaultPath(RegistryRelativePath);
        if (!System.IO.File.Exists(registryPath))
        {
            return (false, $"Registry not found: {registryPath}", null, [], string.Empty);
        }

        var registryContent = System.IO.File.ReadAllText(registryPath);
        var registryRows = ParseRegistryRows(registryContent.Split('\n'));
        var matchingRows = registryRows
            .Where(row => string.Equals(row.Project, cleanName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matchingRows.Count != 1)
        {
            return (false, $"Expected exactly one registry row for '{cleanName}', found {matchingRows.Count}.", null, [], string.Empty);
        }

        var row = matchingRows[0];
        var sourceFolderPath = CombineVaultPath(row.FolderPath);
        var archiveRelativePath = $"99 Archive/Project Archive/{cleanName}";
        var archiveFolderPath = CombineVaultPath(archiveRelativePath);

        if (!Directory.Exists(sourceFolderPath))
        {
            return (false, $"Project folder not found: {sourceFolderPath}", null, [], sourceFolderPath);
        }

        if (Directory.Exists(archiveFolderPath))
        {
            return (false, $"Archive destination already exists: {archiveFolderPath}", null, [], sourceFolderPath);
        }

        var registryNotesToRemove = FindRegistryNoteLinesForProject(registryContent, cleanName);
        var updatedRegistryContent = RemoveRegistryProject(registryContent, cleanName, out var rowRemoved, out var notesRemoved);
        if (!rowRemoved)
        {
            return (false, $"Registry row for '{cleanName}' could not be removed.", null, [], sourceFolderPath);
        }

        var filesToWrite = new List<FileWritePlan>
        {
            new(registryPath, updatedRegistryContent),
        };

        foreach (var notePath in GetProjectStateNotePaths(row.FolderPath))
        {
            if (!System.IO.File.Exists(notePath))
            {
                continue;
            }

            var existingContent = System.IO.File.ReadAllText(notePath);
            var updatedContent = UpdateFrontmatterStatus(existingContent, "archived");
            filesToWrite.Add(new FileWritePlan(notePath, updatedContent));
        }

        var plannedSteps = new List<string>
        {
            $"Move the project folder from {sourceFolderPath} to {archiveFolderPath}.",
            $"Remove the '{cleanName}' row from {registryPath}.",
            "Update project-local note frontmatter status fields to archived before the folder move.",
        };

        if (notesRemoved > 0)
        {
            plannedSteps.Add($"Remove {notesRemoved} registry note line(s) that still mention {cleanName}.");
        }

        var response = new ArchiveProjectPreviewResponse(
            ProjectName: cleanName,
            SourceFolderPath: sourceFolderPath,
            ArchiveFolderPath: archiveFolderPath,
            RegistryPath: registryPath,
            Summary: "The project will be archived, removed from the registry, and moved under 99 Archive/Project Archive.",
            PlannedSteps: plannedSteps,
            RegistryNotesToRemove: registryNotesToRemove);

        return (true, null, response, filesToWrite, sourceFolderPath);
    }

    private static CardEvaluation EvaluateBacklogCard(CardBlock card, DateOnly today)
    {
        var trigger = ExtractMetadataValue(card.Block, "Trigger");
        if (!string.IsNullOrWhiteSpace(trigger))
        {
            return new CardEvaluation(false, $"Trigger requires confirmation: {trigger.Trim()}");
        }

        var notBefore = ExtractMetadataValue(card.Block, "Not before");
        if (!string.IsNullOrWhiteSpace(notBefore))
        {
            if (!DateOnly.TryParse(notBefore.Trim(), out var parsedDate))
            {
                return new CardEvaluation(false, $"Malformed Not before date: {notBefore.Trim()}");
            }

            if (parsedDate > today)
            {
                return new CardEvaluation(false, $"Not before date has not arrived: {parsedDate:yyyy-MM-dd}");
            }
        }

        if (Regex.IsMatch(card.Block, @"\b(blocked|waiting)\b", RegexOptions.IgnoreCase))
        {
            return new CardEvaluation(false, "Card text marks this item blocked or waiting.");
        }

        return new CardEvaluation(true, "Eligible to move into Ready.");
    }

    private static string? ExtractMetadataValue(string block, string label)
    {
        var match = Regex.Match(
            block,
            $@"(?im)^-\s*{Regex.Escape(label)}:\s*(.+)$");

        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static HashSet<string> ParseKanbanCardIds(string kanbanContent)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var heading in new[] { "Ready", "Doing", "Blocked", "Done" })
        {
            var body = ExtractSectionBody(kanbanContent, heading);
            if (body is null)
            {
                continue;
            }

            foreach (var card in ParseCardBlocks(body))
            {
                ids.Add(card.Id);
            }
        }

        return ids;
    }

    private static List<CardBlock> ParseCardBlocks(string sectionBody)
    {
        var trimmed = sectionBody.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || string.Equals(trimmed, "_None._", StringComparison.Ordinal))
        {
            return [];
        }

        var matches = Regex.Matches(sectionBody, @"^###\s+(.+)$", RegexOptions.Multiline);
        var cards = new List<CardBlock>();

        for (var index = 0; index < matches.Count; index++)
        {
            var match = matches[index];
            var start = match.Index;
            var end = index + 1 < matches.Count ? matches[index + 1].Index : sectionBody.Length;
            var block = sectionBody[start..end].Trim('\r', '\n');
            var heading = match.Groups[1].Value.Trim();
            var commaIndex = heading.IndexOf(',');
            var id = commaIndex >= 0 ? heading[..commaIndex].Trim() : heading;
            var title = commaIndex >= 0 ? heading[(commaIndex + 1)..].Trim() : string.Empty;

            cards.Add(new CardBlock(id, title, block));
        }

        return cards;
    }

    private static string BuildCardSectionBody(IReadOnlyList<CardBlock> cards) =>
        cards.Count == 0
            ? "_None._"
            : string.Join("\n\n", cards.Select(card => card.Block.Trim('\r', '\n')));

    private static string BuildReadySectionBody(string existingReadyBody, IReadOnlyList<CardBlock> cardsToMove)
    {
        var movedBody = string.Join("\n\n", cardsToMove.Select(card => card.Block.Trim('\r', '\n')));
        var trimmedExisting = existingReadyBody.Trim();

        if (string.IsNullOrWhiteSpace(trimmedExisting) || string.Equals(trimmedExisting, "_None._", StringComparison.Ordinal))
        {
            return movedBody;
        }

        return trimmedExisting + "\n\n" + movedBody;
    }

    private static string? ExtractSectionBody(string content, string heading)
    {
        var match = Regex.Match(content, $@"^## {Regex.Escape(heading)}\s*$", RegexOptions.Multiline);
        if (!match.Success)
        {
            return null;
        }

        var bodyStart = match.Index + match.Length;
        var nextHeading = Regex.Match(content[bodyStart..], @"^## .+$", RegexOptions.Multiline);
        var bodyEnd = nextHeading.Success ? bodyStart + nextHeading.Index : content.Length;
        return content[bodyStart..bodyEnd].Trim('\r', '\n');
    }

    private static SectionBodyParts SplitTrailingFooter(string sectionBody)
    {
        var trimmed = sectionBody.Trim('\r', '\n');
        var footerMatch = Regex.Match(trimmed, @"(?ms)^(?<body>.*?)(?:\n\n(?<footer>(?:#(?!#)\S.*(?:\n|$))+))$");
        if (!footerMatch.Success)
        {
            return new SectionBodyParts(trimmed, string.Empty);
        }

        return new SectionBodyParts(
            footerMatch.Groups["body"].Value.Trim('\r', '\n'),
            footerMatch.Groups["footer"].Value.Trim('\r', '\n'));
    }

    private static string ReplaceSectionBody(string content, string heading, string newBody)
    {
        var match = Regex.Match(content, $@"^## {Regex.Escape(heading)}\s*$", RegexOptions.Multiline);
        if (!match.Success)
        {
            throw new InvalidOperationException($"Could not find section '## {heading}'.");
        }

        var bodyStart = match.Index + match.Length;
        var nextHeading = Regex.Match(content[bodyStart..], @"^## .+$", RegexOptions.Multiline);
        var bodyEnd = nextHeading.Success ? bodyStart + nextHeading.Index : content.Length;

        var prefix = content[..bodyStart].TrimEnd('\r', '\n');
        var suffix = content[bodyEnd..].TrimStart('\r', '\n');
        return string.IsNullOrWhiteSpace(suffix)
            ? prefix + "\n\n" + newBody.Trim('\r', '\n') + "\n"
            : prefix + "\n\n" + newBody.Trim('\r', '\n') + "\n\n" + suffix;
    }

    private static string UpdateRegistryCadence(string registryContent, string projectName, string cadence, out bool changed)
    {
        var lines = registryContent.Split('\n').ToList();
        var tableStart = lines.FindIndex(line => line.StartsWith("| Project", StringComparison.Ordinal));
        changed = false;

        if (tableStart < 0 || tableStart + 1 >= lines.Count)
        {
            return registryContent;
        }

        for (var index = tableStart + 2; index < lines.Count; index++)
        {
            if (!lines[index].StartsWith('|'))
            {
                break;
            }

            var cells = ParseTableCells(lines[index]);
            if (cells.Count < 4 || !string.Equals(cells[0], projectName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.Equals(cells[3], cadence, StringComparison.OrdinalIgnoreCase))
            {
                return registryContent;
            }

            cells[3] = cadence;
            lines[index] = "| " + string.Join(" | ", cells) + " |";
            changed = true;
            return string.Join("\n", lines);
        }

        return registryContent;
    }

    private static string UpdateRegistryRowState(string registryContent, string projectName, string status, string cadence, out bool changed)
    {
        var lines = registryContent.Split('\n').ToList();
        var tableStart = lines.FindIndex(line => line.StartsWith("| Project", StringComparison.Ordinal));
        changed = false;

        if (tableStart < 0 || tableStart + 1 >= lines.Count)
        {
            return registryContent;
        }

        for (var index = tableStart + 2; index < lines.Count; index++)
        {
            if (!lines[index].StartsWith('|'))
            {
                break;
            }

            var cells = ParseTableCells(lines[index]);
            if (cells.Count < 4 || !string.Equals(cells[0], projectName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            cells[1] = status;
            cells[3] = cadence;
            lines[index] = "| " + string.Join(" | ", cells) + " |";
            changed = true;
            return string.Join("\n", lines);
        }

        return registryContent;
    }

    private static string RemoveRegistryProject(string registryContent, string projectName, out bool rowRemoved, out int notesRemoved)
    {
        var lines = registryContent.Split('\n').ToList();
        rowRemoved = false;
        notesRemoved = 0;

        var tableStart = lines.FindIndex(line => line.StartsWith("| Project", StringComparison.Ordinal));
        if (tableStart >= 0 && tableStart + 1 < lines.Count)
        {
            for (var index = tableStart + 2; index < lines.Count; index++)
            {
                if (!lines[index].StartsWith('|'))
                {
                    break;
                }

                var cells = ParseTableCells(lines[index]);
                if (cells.Count > 0 && string.Equals(cells[0], projectName, StringComparison.OrdinalIgnoreCase))
                {
                    lines.RemoveAt(index);
                    rowRemoved = true;
                    break;
                }
            }
        }

        var notesHeadingIndex = lines.FindIndex(line => string.Equals(line.Trim(), "## Notes", StringComparison.Ordinal));
        if (notesHeadingIndex >= 0)
        {
            for (var index = lines.Count - 1; index > notesHeadingIndex; index--)
            {
                var line = lines[index].Trim();
                if (!line.StartsWith("-", StringComparison.Ordinal))
                {
                    continue;
                }

                if (line.Contains(projectName, StringComparison.OrdinalIgnoreCase))
                {
                    lines.RemoveAt(index);
                    notesRemoved++;
                }
            }
        }

        return string.Join("\n", lines);
    }

    private static List<string> FindRegistryNoteLinesForProject(string registryContent, string projectName)
    {
        var lines = registryContent.Split('\n').ToList();
        var notesHeadingIndex = lines.FindIndex(line => string.Equals(line.Trim(), "## Notes", StringComparison.Ordinal));
        if (notesHeadingIndex < 0)
        {
            return [];
        }

        return lines
            .Skip(notesHeadingIndex + 1)
            .Where(line => line.TrimStart().StartsWith("-", StringComparison.Ordinal))
            .Where(line => line.Contains(projectName, StringComparison.OrdinalIgnoreCase))
            .Select(line => line.Trim())
            .ToList();
    }

    private static IReadOnlyList<string> GetProjectStateNotePaths(string folderRelativePath)
    {
        var paths = new List<string>();

        foreach (var fileName in new[]
                 {
                     "_index.md",
                     "Project Brief.md",
                     "Project Backlog.md",
                     "Project Kanban.md",
                     "Operating Notes.md",
                     "Product Requirements Document.md",
                 })
        {
            paths.Add(CombineVaultPath(folderRelativePath, fileName));
        }

        return paths;
    }

    private static string UpdateFrontmatterStatus(string content, string status)
    {
        var match = Regex.Match(content, @"\A---\r?\n(?<frontmatter>[\s\S]*?)\r?\n---");
        if (!match.Success)
        {
            return content;
        }

        var frontmatter = match.Groups["frontmatter"].Value;
        var updatedFrontmatter = Regex.IsMatch(frontmatter, @"(?im)^status:\s*.*$")
            ? Regex.Replace(frontmatter, @"(?im)^status:\s*.*$", $"status: \"{status}\"")
            : frontmatter.TrimEnd() + $"\nstatus: \"{status}\"";

        return "---\n"
               + updatedFrontmatter.TrimEnd('\r', '\n')
               + "\n---"
               + content[match.Length..];
    }

    private static IReadOnlyList<RegistryRow> ParseRegistryRows(IEnumerable<string> lines)
    {
        var lineList = lines.ToList();
        var tableStart = lineList.FindIndex(line => line.StartsWith("| Project", StringComparison.Ordinal));
        if (tableStart < 0 || tableStart + 1 >= lineList.Count)
        {
            return [];
        }

        var headers = ParseTableCells(lineList[tableStart]);
        var rows = new List<RegistryRow>();

        for (var index = tableStart + 2; index < lineList.Count; index++)
        {
            var line = lineList[index];
            if (!line.StartsWith('|'))
            {
                break;
            }

            var cells = ParseTableCells(line);
            if (cells.Count != headers.Count)
            {
                continue;
            }

            var values = headers.Zip(cells).ToDictionary(pair => pair.First, pair => pair.Second, StringComparer.OrdinalIgnoreCase);
            rows.Add(new RegistryRow(
                Project: GetValue(values, "Project"),
                Status: GetValue(values, "Status"),
                Priority: GetValue(values, "Priority"),
                Cadence: GetValue(values, "Cadence"),
                SessionOrThread: GetValue(values, "Session / thread"),
                FolderPath: UnwrapCode(GetValue(values, "Folder path")),
                BacklogPath: UnwrapCode(GetValue(values, "Backlog path")),
                KanbanPath: UnwrapCode(GetValue(values, "Kanban path"))));
        }

        return rows;
    }

    private static List<string> ParseTableCells(string line) =>
        line.Trim().Trim('|').Split('|').Select(cell => cell.Trim()).ToList();

    private static string GetValue(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) ? value : string.Empty;

    private static string UnwrapCode(string value) => value.Trim().Trim('`');

    private static string BuildProjectBrief(string projectName, string created, string summary) => $"---\ntype: \"project\"\nstatus: \"active\"\ncreated: \"{created}\"\nowner: \"johnny-5\"\nscope: \"project-local\"\nproject: \"{projectName}\"\n---\n# Project Brief: {projectName}\n\n## Summary\n{summary}\n\n## Current status\n- Project scaffold created by Mission Control.\n- Local project boards now exist and are ready for the first planning pass.\n\n## Immediate next move\nAdd the first real backlog item or kickoff note, then begin using the local Kanban as the execution surface.\n\n#{ToTag(projectName)} #project #workflow #product\n";

    private static string BuildProjectBacklog(string projectName, string created) => $"---\ntype: \"project\"\nstatus: \"active\"\ncreated: \"{created}\"\nowner: \"johnny-5\"\nscope: \"project-local\"\nproject: \"{projectName}\"\n---\n# Project Backlog: {projectName}\n\n## Purpose\nOrdered list of project-local outcomes that are not yet active on the project Kanban.\n\n## Priority rules\n- Higher in the file means higher priority.\n- Keep items outcome-focused.\n- Use `Not before: YYYY-MM-DD` and `Trigger:` only when gating is real.\n- Once a card is pulled into the project Kanban, delete it from this backlog immediately.\n- The eventual project session should be the normal writer for this board.\n\n## Backlog\n\n_None._\n\n#{ToTag(projectName)} #project #workflow #backlog\n";

    private static string BuildProjectKanban(string projectName, string created) => $"---\ntype: \"project\"\nstatus: \"active\"\ncreated: \"{created}\"\nowner: \"johnny-5\"\nscope: \"project-local\"\nproject: \"{projectName}\"\n---\n# Project Kanban: {projectName}\n\n## Purpose\nExecution board for active {projectName} work.\n\n## Operating rules\n- The eventual project session should be the normal writer for this board.\n- Read `Doing` first, then `Ready`. Read the local backlog only during explicit sprint fill or board reconciliation.\n- `Ready` is the current sprint queue. Do not just-in-time pull the next backlog item during normal execution.\n- Keep `Doing` limited to 1 to 3 items total.\n- Move blocked work to `Blocked` with a concrete blocker.\n- Keep project context notes separate from the execution board.\n\n## Visual board view\n\n```dataviewjs\nconst file = app.vault.getAbstractFileByPath(dv.current().file.path);\n\nif (!file) {{\n  dv.paragraph(\"*Could not read this Kanban file.*\");\n}} else {{\n  const content = await app.vault.cachedRead(file);\n  const laneOrder = [\"Ready\", \"Doing\", \"Blocked\", \"Done\"];\n\n  const escapeHtml = (value) => String(value)\n    .replace(/&/g, \"&amp;\")\n    .replace(/</g, \"&lt;\")\n    .replace(/>/g, \"&gt;\")\n    .replace(/\\\"/g, \"&quot;\")\n    .replace(/'/g, \"&#39;\");\n\n  function parseLaneBodies(source) {{\n    const headingRe = /^## (Ready|Doing|Blocked|Done)$/gm;\n    const matches = [...source.matchAll(headingRe)];\n    const sections = {{}};\n\n    matches.forEach((match, index) => {{\n      const lane = match[1];\n      const start = match.index + match[0].length;\n      const end = index + 1 < matches.length ? matches[index + 1].index : source.length;\n      sections[lane] = source.slice(start, end).trim();\n    }});\n\n    return sections;\n  }}\n\n  function extractCards(body) {{\n    if (!body || body === \"_None._\") return [];\n    return [...body.matchAll(/^###\\s+(.+)$/gm)].map((match) => {{\n      const heading = match[1].trim();\n      const comma = heading.indexOf(\",\");\n      return {{\n        id: comma >= 0 ? heading.slice(0, comma).trim() : heading,\n        title: comma >= 0 ? heading.slice(comma + 1).trim() : \"\"\n      }};\n    }});\n  }}\n\n  const laneBodies = parseLaneBodies(content);\n  const lanes = laneOrder\n    .map((name) => ({{ name, cards: extractCards(laneBodies[name] ?? \"\") }}))\n    .filter((lane) => lane.cards.length > 0);\n\n  const wrapper = dv.el(\"div\", \"\", {{ cls: \"generated-kanban-root\" }});\n\n  if (!lanes.length) {{\n    wrapper.innerHTML = `<div class=\"generated-kanban-empty\"><em>No cards yet.</em></div>`;\n  }} else {{\n    wrapper.innerHTML = `\n      <style>\n        .generated-kanban-root {{\n          margin: 8px 0 18px;\n        }}\n        .generated-kanban {{\n          display: grid;\n          grid-template-columns: repeat(auto-fit, minmax(250px, 1fr));\n          gap: 14px;\n          align-items: start;\n        }}\n        .generated-kanban-lane {{\n          border: 1px solid var(--background-modifier-border);\n          border-top-width: 3px;\n          border-radius: 14px;\n          background: var(--background-secondary);\n          overflow: hidden;\n          box-shadow: 0 1px 2px rgba(0, 0, 0, 0.04);\n        }}\n        .generated-kanban-lane[data-lane=\"Ready\"] {{\n          border-top-color: var(--color-blue, #4f8cff);\n        }}\n        .generated-kanban-lane[data-lane=\"Doing\"] {{\n          border-top-color: var(--color-orange, #d68b2c);\n        }}\n        .generated-kanban-lane[data-lane=\"Blocked\"] {{\n          border-top-color: var(--color-red, #d14b4b);\n        }}\n        .generated-kanban-lane[data-lane=\"Done\"] {{\n          border-top-color: var(--color-green, #3aa675);\n        }}\n        .generated-kanban-lane-header {{\n          display: flex;\n          align-items: center;\n          justify-content: space-between;\n          gap: 8px;\n          padding: 10px 12px;\n          background: var(--background-primary);\n          border-bottom: 1px solid var(--background-modifier-border);\n        }}\n        .generated-kanban-lane-title {{\n          margin: 0;\n          font-size: 0.95em;\n          font-weight: 700;\n          letter-spacing: 0.01em;\n        }}\n        .generated-kanban-lane-count {{\n          font-size: 0.75em;\n          color: var(--text-muted);\n          background: var(--background-modifier-hover);\n          border: 1px solid var(--background-modifier-border);\n          border-radius: 999px;\n          padding: 2px 8px;\n          white-space: nowrap;\n        }}\n        .generated-kanban-card-list {{\n          display: grid;\n          gap: 8px;\n          padding: 12px;\n          max-height: 420px;\n          overflow: auto;\n        }}\n        .generated-kanban-card {{\n          border: 1px solid var(--background-modifier-border);\n          border-radius: 12px;\n          background: var(--background-primary);\n          padding: 9px 10px;\n        }}\n        .generated-kanban-card-id {{\n          font-size: 0.78em;\n          font-weight: 700;\n          color: var(--text-muted);\n          letter-spacing: 0.02em;\n        }}\n        .generated-kanban-card-title {{\n          margin-top: 4px;\n          line-height: 1.35;\n        }}\n        .generated-kanban-empty {{\n          color: var(--text-muted);\n          margin: 8px 0 16px;\n        }}\n        @media (max-width: 720px) {{\n          .generated-kanban {{\n            grid-template-columns: 1fr;\n          }}\n          .generated-kanban-card-list {{\n            max-height: none;\n          }}\n        }}\n      </style>\n      <div class=\"generated-kanban\">\n        ${'{'}lanes.map((lane) => {{\n          const countLabel = lane.cards.length === 1 ? \"1 card\" : `${'{'}lane.cards.length{'}'} cards`;\n          return `\n            <section class=\"generated-kanban-lane\" data-lane=\"${'{'}escapeHtml(lane.name){'}'}\">\n              <div class=\"generated-kanban-lane-header\">\n                <div class=\"generated-kanban-lane-title\">${'{'}escapeHtml(lane.name){'}'}</div>\n                <div class=\"generated-kanban-lane-count\">${'{'}countLabel{'}'}</div>\n              </div>\n              <div class=\"generated-kanban-card-list\">\n                ${'{'}lane.cards.map((card) => `\n                  <article class=\"generated-kanban-card\">\n                    <div class=\"generated-kanban-card-id\">${'{'}escapeHtml(card.id){'}'}</div>\n                    <div class=\"generated-kanban-card-title\">${'{'}escapeHtml(card.title){'}'}</div>\n                  </article>\n                `).join(\"\"){'}'}\n              </div>\n            </section>\n          `;\n        }}).join(\"\"){'}'}\n      </div>\n    `;\n  }}\n}}\n```\n\n## Ready\n\n_None._\n\n## Doing\n\n_None._\n\n## Blocked\n\n_None._\n\n## Done\n\n_None._\n\n#{ToTag(projectName)} #project #workflow #kanban\n";

    private static string BuildOperatingNotes(string projectName, string created) => $"---\ntype: \"project-note\"\nstatus: \"active\"\ncreated: \"{created}\"\nproject: \"{projectName}\"\n---\n# Operating Notes: {projectName}\n\n## Guardrails\n- Obsidian remains the source of truth for backlog, decisions, and planning.\n- Keep side effects explicit and legible before execution.\n- Prefer local project boards for execution detail.\n\n## Early implementation posture\n- Start with one small useful slice.\n- Keep project-local markdown human-readable.\n- Promote reusable rules out of the project once they stabilize.\n\n## Operational note\n- Add the project session or thread here once routing is established.\n\n#{ToTag(projectName)} #ops #workflow #notes\n";

    private static string BuildRegistryRow(string projectName, string folderRelativePath, string created)
    {
        var escapedFolder = WrapCode(folderRelativePath + "/");
        var backlogPath = WrapCode(folderRelativePath + "/Project Backlog.md");
        var kanbanPath = WrapCode(folderRelativePath + "/Project Kanban.md");

        return $"| {projectName} | active | P1 | manual until first local work appears | not yet assigned | {escapedFolder} | {backlogPath} | {kanbanPath} | {created} | {created} | none | no |";
    }

    private static void AppendRegistryRow(string registryPath, string registryRow)
    {
        var content = System.IO.File.ReadAllText(registryPath);
        const string notesHeading = "\n## Notes";
        var notesIndex = content.IndexOf(notesHeading, StringComparison.Ordinal);
        if (notesIndex < 0)
        {
            throw new InvalidOperationException("Could not find the registry notes section.");
        }

        var updated = content.Insert(notesIndex, registryRow + "\n");
        System.IO.File.WriteAllText(registryPath, updated);
    }

    private static bool RegistryContainsProject(string registryContent, string projectName)
    {
        var lines = registryContent.Split('\n');
        foreach (var line in lines)
        {
            if (!line.StartsWith('|'))
            {
                continue;
            }

            var cells = line.Trim().Trim('|').Split('|').Select(cell => cell.Trim()).ToList();
            if (cells.Count > 0 && string.Equals(cells[0], projectName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string WrapCode(string value) => $"`{value}`";

    private static string ToTag(string projectName) =>
        Regex.Replace(projectName.Trim().ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');

    private static string? ResolveRepoPath(string projectName)
    {
        var slug = ToTag(projectName);

        if (string.IsNullOrWhiteSpace(slug))
        {
            return null;
        }

        var repoPath = Path.Combine(ReposRoot, slug);
        return Directory.Exists(repoPath) ? repoPath : null;
    }

    private static async Task<ActionCommandResult> RunCommand(
        string workingDirectory,
        string command,
        CancellationToken cancellationToken)
    {
        var output = new StringBuilder();
        var stopwatch = Stopwatch.StartNew();

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "/usr/bin/env",
                ArgumentList = { "bash", "-lc", command },
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            },
        };

        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                output.AppendLine(args.Data);
            }
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                output.AppendLine(args.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken);
        stopwatch.Stop();

        return new ActionCommandResult(
            Command: command,
            ExitCode: process.ExitCode,
            DurationMs: stopwatch.ElapsedMilliseconds,
            Output: output.ToString().Trim(),
            Success: process.ExitCode == 0);
    }

    private static string CombineVaultPath(params string[] relativeParts)
    {
        var relativePath = string.Join('/', relativeParts.Where(part => !string.IsNullOrWhiteSpace(part)));
        return Path.Combine(VaultRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private sealed record FileWritePlan(string Path, string Content);

    private sealed record CardBlock(string Id, string Title, string Block);

    private sealed record CardEvaluation(bool Eligible, string Reason);

    private sealed record SectionBodyParts(string Body, string Footer);

    private sealed record RegistryRow(
        string Project,
        string Status,
        string Priority,
        string Cadence,
        string SessionOrThread,
        string FolderPath,
        string BacklogPath,
        string KanbanPath);
}

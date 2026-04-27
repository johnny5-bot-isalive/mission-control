using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace backend.Controllers;

public sealed record ProjectLinkResponse(string Label, string Url);

public sealed record ProjectSummaryResponse(
    string Name,
    string Status,
    string Priority,
    string Cadence,
    string Summary,
    IReadOnlyList<ProjectLinkResponse> Links);

[ApiController]
[Route("api/[controller]")]
public sealed class ProjectsController : ControllerBase
{
    private const string VaultName = "The Nexus";
    private const string DefaultVaultRoot = "/mnt/c/Users/Jaret/Obsidian/The Nexus";
    private const string RegistryRelativePath = "40 Agent Nexus/Project Registry.md";
    private const string RegistryDirectoryRelativePath = "40 Agent Nexus";
    private static readonly Regex MarkdownLinkRegex = new(@"^\[(?<text>.+)\]\((?<target>[^)]+)\)$", RegexOptions.Compiled);

    private static string VaultRoot =>
        Environment.GetEnvironmentVariable("MISSION_CONTROL_VAULT_ROOT") ?? DefaultVaultRoot;

    /// <summary>
    /// Lists active and inactive projects from the registry, enriched with markdown summary text and primary note links.
    /// </summary>
    [HttpGet]
    [SwaggerOperation(Summary = "Lists active and inactive projects from the registry, enriched with markdown summary text and primary note links.")]
    public ActionResult<IReadOnlyList<ProjectSummaryResponse>> Get()
    {
        var registryPath = Path.Combine(VaultRoot, RegistryRelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!System.IO.File.Exists(registryPath))
        {
            return Problem($"Registry not found: {registryPath}", statusCode: StatusCodes.Status500InternalServerError);
        }

        var rows = ParseRegistryRows(System.IO.File.ReadAllLines(registryPath));
        var projects = rows
            .Where(row => !string.Equals(row.Status, "archived", StringComparison.OrdinalIgnoreCase))
            .Select(BuildProjectSummary)
            .ToList();

        return Ok(projects);
    }

    private static ProjectSummaryResponse BuildProjectSummary(RegistryRow row)
    {
        var summary = ReadSummary(row.FolderPath);
        var links = BuildLinks(row);

        return new ProjectSummaryResponse(
            Name: row.Project,
            Status: row.Status,
            Priority: row.Priority,
            Cadence: row.Cadence,
            Summary: string.IsNullOrWhiteSpace(summary)
                ? "No summary found in Project Brief.md."
                : summary,
            Links: links);
    }

    private static string ReadSummary(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return string.Empty;
        }

        var briefPath = CombineVaultPath(folderPath, "Project Brief.md");
        if (!System.IO.File.Exists(briefPath))
        {
            return string.Empty;
        }

        var content = System.IO.File.ReadAllText(briefPath);
        var match = Regex.Match(
            content,
            @"^## Summary\s*(?<body>[\s\S]*?)(?=^##\s|\z)",
            RegexOptions.Multiline);

        if (!match.Success)
        {
            return string.Empty;
        }

        var lines = match.Groups["body"].Value
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => !line.StartsWith("#", StringComparison.Ordinal));

        return string.Join(" ", lines);
    }

    private static IReadOnlyList<ProjectLinkResponse> BuildLinks(RegistryRow row)
    {
        var links = new List<ProjectLinkResponse>();

        TryAddVaultLink(links, "Brief", CombineRelativePath(row.FolderPath, "Project Brief.md"));
        TryAddVaultLink(links, "Kanban", row.KanbanPath);
        TryAddVaultLink(links, "Backlog", row.BacklogPath);
        TryAddVaultLink(links, "PRD", CombineRelativePath(row.FolderPath, "Product Requirements Document.md"));
        TryAddVaultLink(links, "Operating", CombineRelativePath(row.FolderPath, "Operating Notes.md"));

        return links;
    }

    private static void TryAddVaultLink(ICollection<ProjectLinkResponse> links, string label, string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return;
        }

        var absolutePath = CombineVaultPath(relativePath);
        if (!System.IO.File.Exists(absolutePath))
        {
            return;
        }

        var encodedVault = Uri.EscapeDataString(VaultName);
        var encodedFile = Uri.EscapeDataString(relativePath.Replace('\\', '/'));
        links.Add(new ProjectLinkResponse(label, $"obsidian://open?vault={encodedVault}&file={encodedFile}"));
    }

    private static string CombineRelativePath(string basePath, string child)
    {
        if (string.IsNullOrWhiteSpace(basePath))
        {
            return child;
        }

        return $"{basePath.TrimEnd('/')}/{child}";
    }

    private static string CombineVaultPath(params string[] relativeParts)
    {
        var relativePath = string.Join('/', relativeParts.Where(part => !string.IsNullOrWhiteSpace(part)));
        return Path.Combine(VaultRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
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
                FolderPath: ParseRegistryPathValue(GetValue(values, "Folder path")),
                BacklogPath: ParseRegistryPathValue(GetValue(values, "Backlog path")),
                KanbanPath: ParseRegistryPathValue(GetValue(values, "Kanban path"))));
        }

        return rows;
    }

    private static List<string> ParseTableCells(string line) =>
        line.Trim().Trim('|').Split('|').Select(cell => cell.Trim()).ToList();

    private static string GetValue(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) ? value : string.Empty;

    private static string ParseRegistryPathValue(string value)
    {
        var trimmed = value.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        var linkMatch = MarkdownLinkRegex.Match(trimmed);
        if (linkMatch.Success)
        {
            var linkText = NormalizeRelativePath(UnwrapCode(linkMatch.Groups["text"].Value));
            if (!string.IsNullOrWhiteSpace(linkText))
            {
                return linkText;
            }

            return ResolveRegistryLinkTarget(linkMatch.Groups["target"].Value);
        }

        return NormalizeRelativePath(UnwrapCode(trimmed));
    }

    private static string ResolveRegistryLinkTarget(string target)
    {
        var cleanTarget = Uri.UnescapeDataString(target.Trim().Trim('<', '>'));
        if (string.IsNullOrWhiteSpace(cleanTarget))
        {
            return string.Empty;
        }

        cleanTarget = cleanTarget.Split('#', 2)[0].Split('?', 2)[0];
        if (string.IsNullOrWhiteSpace(cleanTarget))
        {
            return string.Empty;
        }

        if (Uri.TryCreate(cleanTarget, UriKind.Absolute, out _))
        {
            return string.Empty;
        }

        var baseUri = new Uri($"https://vault.local/{RegistryDirectoryRelativePath.Trim('/')}/");
        var resolved = new Uri(baseUri, cleanTarget);
        return NormalizeRelativePath(Uri.UnescapeDataString(resolved.AbsolutePath.TrimStart('/')));
    }

    private static string NormalizeRelativePath(string value)
    {
        var normalized = value.Replace('\\', '/').Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        return string.Join('/', normalized.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static string UnwrapCode(string value) => value.Trim().Trim('`');

    private sealed record RegistryRow(
        string Project,
        string Status,
        string Priority,
        string Cadence,
        string FolderPath,
        string BacklogPath,
        string KanbanPath);
}

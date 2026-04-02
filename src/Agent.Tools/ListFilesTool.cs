using System.Text.Json;
using Agent.Core.Tools;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;

namespace Agent.Tools;

public class ListFilesTool : ITool
{
    public string Name => "ListFiles";
    public string Description => "List files in a directory matching a glob pattern";
    public ToolPolicy Policy => new() { IsReadOnly = true, IsConcurrencySafe = true };

    public string InputSchema => """
    {
        "type": "object",
        "properties": {
            "glob": { "type": "string", "description": "Glob pattern (e.g., **/*.cs)" },
            "path": { "type": "string", "description": "Subdirectory to search in (relative, default: repo root)" },
            "sortBy": { "type": "string", "enum": ["name", "modified"], "description": "Sort order (default: name)" }
        },
        "required": ["glob"]
    }
    """;

    public Task<ToolResult> ExecuteAsync(JsonElement args, ToolContext ctx, CancellationToken ct)
    {
        var glob = args.GetProperty("glob").GetString()!;
        var subPath = args.TryGetProperty("path", out var p) ? p.GetString() : null;
        var sortBy = args.TryGetProperty("sortBy", out var s) ? s.GetString() : "name";

        var searchRoot = ctx.RepoRoot;
        if (!string.IsNullOrWhiteSpace(subPath))
        {
            var resolved = ctx.Workspace.ResolvePath(subPath);
            if (resolved == null)
            {
                return Task.FromResult(ToolResult.Failure($"Path outside repo: {subPath}"));
            }
            if (!Directory.Exists(resolved))
            {
                return Task.FromResult(ToolResult.Failure($"Directory not found: {subPath}"));
            }
            searchRoot = resolved;
        }

        var matcher = new Matcher();
        matcher.AddInclude(glob);

        // Add common exclusions
        matcher.AddExclude("**/node_modules/**");
        matcher.AddExclude("**/.git/**");
        matcher.AddExclude("**/bin/**");
        matcher.AddExclude("**/obj/**");

        var result = matcher.Execute(new DirectoryInfoWrapper(new DirectoryInfo(searchRoot)));

        // Prefix subPath to make paths repo-root-relative (Matcher returns paths relative to searchRoot)
        var prefix = !string.IsNullOrWhiteSpace(subPath) ? subPath.TrimEnd('/').Replace('\\', '/') + "/" : "";
        IEnumerable<string> filePaths = result.Files.Select(f => prefix + f.Path.Replace('\\', '/'));

        if (sortBy == "modified")
        {
            filePaths = filePaths
                .Select(f => (path: f, mtime: File.GetLastWriteTimeUtc(Path.Combine(ctx.RepoRoot, f))))
                .OrderByDescending(x => x.mtime)
                .Select(x => x.path);
        }

        var files = filePaths.Take(500).ToList();

        return Task.FromResult(ToolResult.Success(new
        {
            pattern = glob,
            searchRoot = searchRoot == ctx.RepoRoot ? "." : subPath,
            count = files.Count,
            files
        }));
    }
}

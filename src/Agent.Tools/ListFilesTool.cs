using System.Text.Json;
using Agent.Core.Tools;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;

namespace Agent.Tools;

public class ListFilesTool : ITool
{
    public string Name => "ListFiles";
    public string Description => "List files in a directory matching a glob pattern";
    public ToolPolicy Policy => new() { IsReadOnly = true };

    public string InputSchema => """
    {
        "type": "object",
        "properties": {
            "glob": { "type": "string", "description": "Glob pattern (e.g., **/*.cs)" },
            "maxDepth": { "type": "integer", "default": 10 }
        },
        "required": ["glob"]
    }
    """;

    public Task<ToolResult> ExecuteAsync(JsonElement args, ToolContext ctx, CancellationToken ct)
    {
        var glob = args.GetProperty("glob").GetString()!;

        var matcher = new Matcher();
        matcher.AddInclude(glob);

        // Add common exclusions
        matcher.AddExclude("**/node_modules/**");
        matcher.AddExclude("**/.git/**");
        matcher.AddExclude("**/bin/**");
        matcher.AddExclude("**/obj/**");

        var result = matcher.Execute(new DirectoryInfoWrapper(new DirectoryInfo(ctx.RepoRoot)));

        var files = result.Files
            .Select(f => f.Path.Replace('\\', '/'))
            .Take(500)
            .ToList();

        return Task.FromResult(ToolResult.Success(new
        {
            pattern = glob,
            count = files.Count,
            files
        }));
    }
}

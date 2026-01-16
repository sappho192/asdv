using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Agent.Core.Tools;

namespace Agent.Tools;

public class SearchTextTool : ITool
{
    public string Name => "SearchText";
    public string Description => "Search for text patterns in the repository";
    public ToolPolicy Policy => new() { IsReadOnly = true };

    public string InputSchema => """
    {
        "type": "object",
        "properties": {
            "query": { "type": "string", "description": "Search pattern (regex supported)" },
            "includeGlobs": { "type": "array", "items": { "type": "string" }, "description": "Glob patterns to include" },
            "excludeGlobs": { "type": "array", "items": { "type": "string" }, "description": "Glob patterns to exclude" },
            "maxResults": { "type": "integer", "default": 50 }
        },
        "required": ["query"]
    }
    """;

    private static readonly string[] SkipDirs = [".git", "node_modules", "bin", "obj", ".vs", "__pycache__", ".idea"];
    private static readonly string[] BinaryExtensions = [".exe", ".dll", ".so", ".dylib", ".png", ".jpg", ".jpeg", ".gif", ".ico", ".pdf", ".zip", ".tar", ".gz"];

    public async Task<ToolResult> ExecuteAsync(JsonElement args, ToolContext ctx, CancellationToken ct)
    {
        var query = args.GetProperty("query").GetString()!;
        var maxResults = args.TryGetProperty("maxResults", out var m) ? m.GetInt32() : 50;

        // Try ripgrep first if available
        var rgPath = FindRipgrep();
        if (rgPath != null)
        {
            return await SearchWithRipgrepAsync(rgPath, query, ctx.RepoRoot, maxResults, ct);
        }

        // Fallback to manual search
        return await SearchManuallyAsync(query, ctx.RepoRoot, maxResults, ct);
    }

    private static string? FindRipgrep()
    {
        var candidates = new[] { "rg", "rg.exe" };
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        var paths = pathEnv.Split(Path.PathSeparator);

        foreach (var candidate in candidates)
        {
            foreach (var path in paths)
            {
                var fullPath = Path.Combine(path, candidate);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }
        }

        return null;
    }

    private async Task<ToolResult> SearchWithRipgrepAsync(
        string rgPath, string query, string root, int maxResults, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = rgPath,
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        psi.ArgumentList.Add("--json");
        psi.ArgumentList.Add("-m");
        psi.ArgumentList.Add(maxResults.ToString());
        psi.ArgumentList.Add("--");
        psi.ArgumentList.Add(query);

        using var process = Process.Start(psi)!;
        var output = await process.StandardOutput.ReadToEndAsync(ct);
        var error = await process.StandardError.ReadToEndAsync(ct);

        await process.WaitForExitAsync(ct);

        var matches = ParseRipgrepOutput(output, maxResults);

        return ToolResult.Success(new
        {
            tool = "ripgrep",
            query,
            count = matches.Count,
            matches
        });
    }

    private static List<object> ParseRipgrepOutput(string output, int maxResults)
    {
        var matches = new List<object>();
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            if (matches.Count >= maxResults) break;

            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;

                if (root.TryGetProperty("type", out var typeEl) && typeEl.GetString() == "match")
                {
                    var data = root.GetProperty("data");
                    var path = data.GetProperty("path").GetProperty("text").GetString();
                    var lineNumber = data.GetProperty("line_number").GetInt32();
                    var lineText = data.GetProperty("lines").GetProperty("text").GetString()?.TrimEnd('\n', '\r');

                    matches.Add(new
                    {
                        file = path,
                        line = lineNumber,
                        content = lineText
                    });
                }
            }
            catch
            {
                // Skip invalid JSON lines
            }
        }

        return matches;
    }

    private async Task<ToolResult> SearchManuallyAsync(
        string query, string root, int maxResults, CancellationToken ct)
    {
        Regex regex;
        try
        {
            regex = new Regex(query, RegexOptions.IgnoreCase | RegexOptions.Compiled);
        }
        catch (ArgumentException ex)
        {
            return ToolResult.Failure($"Invalid regex pattern: {ex.Message}");
        }

        var results = new List<object>();

        await foreach (var file in EnumerateFilesAsync(root, ct))
        {
            if (results.Count >= maxResults) break;

            try
            {
                var lines = await File.ReadAllLinesAsync(file, ct);
                for (int i = 0; i < lines.Length && results.Count < maxResults; i++)
                {
                    if (regex.IsMatch(lines[i]))
                    {
                        results.Add(new
                        {
                            file = Path.GetRelativePath(root, file).Replace('\\', '/'),
                            line = i + 1,
                            content = lines[i].Trim()
                        });
                    }
                }
            }
            catch
            {
                // Skip files that can't be read
            }
        }

        return ToolResult.Success(new
        {
            tool = "manual",
            query,
            count = results.Count,
            matches = results
        });
    }

    private static async IAsyncEnumerable<string> EnumerateFilesAsync(string root, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var queue = new Queue<string>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            ct.ThrowIfCancellationRequested();

            var dir = queue.Dequeue();

            string[] files;
            try
            {
                files = Directory.GetFiles(dir);
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (!BinaryExtensions.Contains(ext))
                {
                    yield return file;
                }
            }

            string[] subdirs;
            try
            {
                subdirs = Directory.GetDirectories(dir);
            }
            catch
            {
                continue;
            }

            foreach (var subdir in subdirs)
            {
                var dirName = Path.GetFileName(subdir);
                if (!SkipDirs.Contains(dirName, StringComparer.OrdinalIgnoreCase))
                {
                    queue.Enqueue(subdir);
                }
            }

            await Task.Yield();
        }
    }
}

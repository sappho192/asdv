using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Agent.Core.Tools;

namespace Agent.Tools;

public class RunCommandTool : ITool
{
    public string Name => "RunCommand";
    public string Description => "Execute a command in the repository directory";
    public ToolPolicy Policy => new() { RequiresApproval = true, Risk = RiskLevel.High };

    public string InputSchema => """
    {
        "type": "object",
        "properties": {
            "exe": { "type": "string", "description": "Executable name or path" },
            "args": { "type": "array", "items": { "type": "string" }, "description": "Command arguments" },
            "cwd": { "type": "string", "description": "Working directory (relative to repo)" },
            "timeoutSec": { "type": "integer", "default": 60, "description": "Timeout in seconds" }
        },
        "required": ["exe"]
    }
    """;

    private const int MaxOutputLength = 50000;

    private static readonly string[] SensitiveEnvKeys =
    [
        "API_KEY", "SECRET", "PASSWORD", "TOKEN", "CREDENTIAL", "PRIVATE_KEY", "AUTH"
    ];

    public async Task<ToolResult> ExecuteAsync(JsonElement args, ToolContext ctx, CancellationToken ct)
    {
        var exe = args.GetProperty("exe").GetString()!;
        var cmdArgs = args.TryGetProperty("args", out var a)
            ? a.EnumerateArray().Select(x => x.GetString()!).ToArray()
            : Array.Empty<string>();
        var timeoutSec = args.TryGetProperty("timeoutSec", out var t) ? t.GetInt32() : 60;

        var cwd = ctx.RepoRoot;
        if (args.TryGetProperty("cwd", out var cwdProp))
        {
            var relativeCwd = cwdProp.GetString();
            if (!string.IsNullOrEmpty(relativeCwd))
            {
                var resolvedCwd = ctx.Workspace.ResolvePath(relativeCwd);
                if (resolvedCwd == null)
                {
                    return ToolResult.Failure($"Invalid working directory: {relativeCwd}");
                }
                cwd = resolvedCwd;
            }
        }

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            WorkingDirectory = cwd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in cmdArgs)
        {
            psi.ArgumentList.Add(arg);
        }

        FilterEnvironment(psi.Environment);

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var stdoutLock = new object();
        var stderrLock = new object();

        using var process = new Process { StartInfo = psi };

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                lock (stdoutLock)
                {
                    if (stdout.Length < MaxOutputLength)
                    {
                        stdout.AppendLine(e.Data);
                    }
                }
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                lock (stderrLock)
                {
                    if (stderr.Length < MaxOutputLength)
                    {
                        stderr.AppendLine(e.Data);
                    }
                }
            }
        };

        var stopwatch = Stopwatch.StartNew();

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            return ToolResult.Failure($"Failed to start process: {ex.Message}");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(timeoutSec));

        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Ignore kill errors
            }

            return ToolResult.Failure($"Command timed out after {timeoutSec}s");
        }

        stopwatch.Stop();

        var result = new
        {
            command = $"{exe} {string.Join(" ", cmdArgs)}",
            exitCode = process.ExitCode,
            durationMs = stopwatch.ElapsedMilliseconds,
            stdoutTruncated = stdout.Length >= MaxOutputLength,
            stderrTruncated = stderr.Length >= MaxOutputLength
        };

        var stdoutStr = stdout.ToString();
        var stderrStr = stderr.ToString();

        if (process.ExitCode == 0)
        {
            return new ToolResult
            {
                Ok = true,
                Data = result,
                Stdout = stdoutStr,
                Stderr = string.IsNullOrEmpty(stderrStr) ? null : stderrStr
            };
        }

        return new ToolResult
        {
            Ok = false,
            Data = result,
            Stdout = stdoutStr,
            Stderr = stderrStr,
            Diagnostics = [new Diagnostic("ExitCode", $"Command exited with code {process.ExitCode}")]
        };
    }

    private static void FilterEnvironment(IDictionary<string, string?> env)
    {
        var keysToRemove = env.Keys
            .Where(k => SensitiveEnvKeys.Any(s => k.Contains(s, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        foreach (var key in keysToRemove)
        {
            env.Remove(key);
        }
    }
}

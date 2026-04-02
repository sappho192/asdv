using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Agent.Core.Tools;

namespace Agent.Tools;

public class RunCommandTool : ITool
{
    public string Name => "RunCommand";
    public string Description => "Execute a shell command in the repository directory";
    public ToolPolicy Policy => new() { RequiresApproval = true, Risk = RiskLevel.High, ProducesProgress = true, IsExternalSideEffect = true };

    public string InputSchema => """
    {
        "type": "object",
        "properties": {
            "command": { "type": "string", "description": "Shell command to execute (e.g. 'ls -la | grep .json')" },
            "cwd": { "type": "string", "description": "Working directory (relative to repo)" },
            "timeoutSec": { "type": "integer", "default": 60, "description": "Timeout in seconds" },
            "shell": { "type": "string", "description": "Override shell (e.g. 'bash', 'powershell'). Auto-detected by default." }
        },
        "required": ["command"]
    }
    """;

    private const int MaxOutputLength = 50000;

    private static readonly string[] SensitiveEnvKeys =
    [
        "API_KEY", "SECRET", "PASSWORD", "TOKEN", "CREDENTIAL", "PRIVATE_KEY", "AUTH"
    ];

    public async Task<ToolResult> ExecuteAsync(JsonElement args, ToolContext ctx, CancellationToken ct)
    {
        var command = args.GetProperty("command").GetString()!;
        var timeoutSec = args.TryGetProperty("timeoutSec", out var t) ? t.GetInt32() : 60;
        var shellOverride = args.TryGetProperty("shell", out var sh) ? sh.GetString() : null;

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

        // Determine shell executable and arguments
        var (shellExe, shellArgs) = ResolveShell(shellOverride);

        var psi = new ProcessStartInfo
        {
            FileName = shellExe,
            WorkingDirectory = cwd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in shellArgs)
            psi.ArgumentList.Add(arg);
        psi.ArgumentList.Add(command);

        FilterEnvironment(psi.Environment);

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var stdoutLock = new object();
        var stderrLock = new object();

        using var process = new Process { StartInfo = psi };

        var lineCount = 0;
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

                var count = Interlocked.Increment(ref lineCount);
                if (count % 10 == 0 && ctx.Progress != null)
                {
                    var preview = e.Data.Length > 80 ? e.Data[..80] + "..." : e.Data;
                    ctx.Progress.Report(new ToolProgressInfo(
                        ctx.CallId ?? "", $"[{count} lines] {preview}"));
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
            command,
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

    /// <summary>
    /// Resolve shell executable and prefix arguments based on OS and optional override.
    /// </summary>
    private static (string Exe, string[] Args) ResolveShell(string? shellOverride)
    {
        if (!string.IsNullOrEmpty(shellOverride))
        {
            return shellOverride.ToLowerInvariant() switch
            {
                "powershell" or "pwsh" => ("pwsh", ["-NoProfile", "-Command"]),
                "cmd" => ("cmd.exe", ["/c"]),
                "bash" => ("bash", ["-c"]),
                "sh" => ("sh", ["-c"]),
                "zsh" => ("zsh", ["-c"]),
                _ => (shellOverride, ["-c"])
            };
        }

        // Auto-detect based on OS
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return ("cmd.exe", ["/c"]);
        }

        // Unix: prefer /bin/sh for portability
        return ("/bin/sh", ["-c"]);
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

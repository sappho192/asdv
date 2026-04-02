using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Agent.Tools;

/// <summary>
/// Detects environment facts at session start: git status, available runtimes, etc.
/// Results are injected into the system prompt.
/// </summary>
public static class EnvironmentDetector
{
    public record EnvironmentInfo(
        bool IsGitRepo,
        bool HasNode,
        bool HasPython,
        string? NodeVersion,
        string? PythonVersion,
        string OsDescription);

    public static EnvironmentInfo Detect(string repoRoot)
    {
        var isGitRepo = Directory.Exists(Path.Combine(repoRoot, ".git"));
        var (hasNode, nodeVersion) = TryGetVersion("node", "--version");
        var (hasPython, pythonVersion) = TryGetVersion("python3", "--version")
            is { Item1: true } result ? result : TryGetVersion("python", "--version");

        return new EnvironmentInfo(
            isGitRepo,
            hasNode,
            hasPython,
            nodeVersion,
            pythonVersion,
            RuntimeInformation.OSDescription);
    }

    public static string FormatForPrompt(EnvironmentInfo env)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Environment");
        sb.AppendLine();

        if (!env.IsGitRepo)
            sb.AppendLine("- **NOT a git repository.** Do not use GitStatus or GitDiff tools.");
        else
            sb.AppendLine("- Git repository detected.");

        if (env.HasNode)
            sb.AppendLine($"- Node.js available ({env.NodeVersion})");
        else
            sb.AppendLine("- Node.js is NOT available.");

        if (env.HasPython)
            sb.AppendLine($"- Python available ({env.PythonVersion})");
        else
            sb.AppendLine("- Python is NOT available.");

        sb.AppendLine($"- OS: {env.OsDescription}");

        return sb.ToString();
    }

    private static (bool, string?) TryGetVersion(string command, string arg)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = command,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add(arg);

            using var process = Process.Start(psi);
            if (process == null) return (false, null);

            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(3000);

            if (process.ExitCode == 0 && !string.IsNullOrEmpty(output))
                return (true, output);

            return (false, null);
        }
        catch
        {
            return (false, null);
        }
    }
}

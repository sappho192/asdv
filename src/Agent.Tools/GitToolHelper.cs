using System.Runtime.InteropServices;

namespace Agent.Tools;

internal static class GitToolHelper
{
    internal static string? ResolveGitPath()
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathEnv))
            return null;

        var candidates = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new[] { "git.exe", "git.cmd", "git.bat", "git" }
            : new[] { "git" };

        foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var name in candidates)
            {
                var candidate = Path.Combine(dir, name);
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return null;
    }
}

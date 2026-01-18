using Agent.Core.Workspace;

namespace Agent.Workspace;

public class LocalWorkspace : IWorkspace
{
    public string Root { get; }
    private readonly string _normalizedRoot;

    public LocalWorkspace(string root)
    {
        Root = Path.GetFullPath(root);
        _normalizedRoot = NormalizePath(Root);
    }

    public string? ResolvePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return null;

        // Prevent absolute paths
        if (Path.IsPathRooted(relativePath) || LooksLikeWindowsAbsolutePath(relativePath))
            return null;

        // Prevent obvious path traversal attempts
        if (relativePath.Contains(".."))
        {
            var combined = Path.Combine(Root, relativePath);
            var fullPath = Path.GetFullPath(combined);
            return IsPathSafe(fullPath) ? fullPath : null;
        }

        var resolvedPath = Path.GetFullPath(Path.Combine(Root, relativePath));
        return IsPathSafe(resolvedPath) ? resolvedPath : null;
    }

    public bool IsPathSafe(string fullPath)
    {
        var normalized = NormalizePath(fullPath);

        try
        {
            if (!IsUnderRoot(normalized))
                return false;

            if (!AreSymlinksSafe(normalized))
                return false;
        }
        catch
        {
            // If we can't check, assume unsafe
            return false;
        }

        return true;
    }

    private bool AreSymlinksSafe(string normalizedPath)
    {
        var relative = Path.GetRelativePath(_normalizedRoot, normalizedPath);
        if (relative == ".")
            return true;

        var parts = relative.Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);

        var current = _normalizedRoot;
        foreach (var part in parts)
        {
            current = Path.Combine(current, part);
            if (!File.Exists(current) && !Directory.Exists(current))
                continue;

            FileSystemInfo info = Directory.Exists(current)
                ? new DirectoryInfo(current)
                : new FileInfo(current);

            if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                var target = info.ResolveLinkTarget(returnFinalTarget: true);
                if (target == null)
                    return false;

                var targetPath = NormalizePath(target.FullName);
                if (!IsUnderRoot(targetPath))
                    return false;
            }
        }

        return true;
    }

    private static bool LooksLikeWindowsAbsolutePath(string path)
    {
        if (path.Length >= 3 &&
            char.IsLetter(path[0]) &&
            path[1] == ':' &&
            (path[2] == '\\' || path[2] == '/'))
            return true;

        if (path.StartsWith(@"\\", StringComparison.Ordinal) ||
            path.StartsWith("//", StringComparison.Ordinal))
            return true;

        return false;
    }

    private bool IsUnderRoot(string normalizedPath)
    {
        // Must be under root
        if (!normalizedPath.StartsWith(_normalizedRoot, StringComparison.OrdinalIgnoreCase))
            return false;

        // Ensure it's actually a subdirectory, not just a prefix match
        if (normalizedPath.Length > _normalizedRoot.Length)
        {
            var separator = normalizedPath[_normalizedRoot.Length];
            if (separator != Path.DirectorySeparatorChar && separator != Path.AltDirectorySeparatorChar)
                return false;
        }

        return true;
    }

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}

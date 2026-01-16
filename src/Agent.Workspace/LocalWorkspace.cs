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
        if (Path.IsPathRooted(relativePath))
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

        // Must be under root
        if (!normalized.StartsWith(_normalizedRoot, StringComparison.OrdinalIgnoreCase))
            return false;

        // Ensure it's actually a subdirectory, not just a prefix match
        if (normalized.Length > _normalizedRoot.Length)
        {
            var separator = normalized[_normalizedRoot.Length];
            if (separator != Path.DirectorySeparatorChar && separator != Path.AltDirectorySeparatorChar)
                return false;
        }

        // Check for symlink escape (if path exists)
        try
        {
            if (File.Exists(fullPath) || Directory.Exists(fullPath))
            {
                var fileInfo = new FileInfo(fullPath);
                if (fileInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    // Resolve symlink target and check if it's safe
                    var target = fileInfo.ResolveLinkTarget(returnFinalTarget: true);
                    if (target != null)
                    {
                        var targetPath = target.FullName;
                        if (!NormalizePath(targetPath).StartsWith(_normalizedRoot, StringComparison.OrdinalIgnoreCase))
                            return false;
                    }
                }
            }
        }
        catch
        {
            // If we can't check, assume unsafe
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

namespace Agent.Core.Workspace;

public interface IWorkspace
{
    string Root { get; }
    string? ResolvePath(string relativePath);
    bool IsPathSafe(string fullPath);
}

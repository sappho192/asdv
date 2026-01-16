using Agent.Workspace;
using FluentAssertions;

namespace Agent.Core.Tests;

public class WorkspaceTests : IDisposable
{
    private readonly string _testRoot;
    private readonly LocalWorkspace _workspace;

    public WorkspaceTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "agent_test_" + Guid.NewGuid());
        Directory.CreateDirectory(_testRoot);
        _workspace = new LocalWorkspace(_testRoot);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_testRoot, recursive: true);
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    [Fact]
    public void ResolvePath_ValidRelative_ReturnsFullPath()
    {
        var result = _workspace.ResolvePath("src/file.cs");

        result.Should().NotBeNull();
        result.Should().Be(Path.Combine(_testRoot, "src", "file.cs"));
    }

    [Fact]
    public void ResolvePath_TraversalAttempt_ReturnsNull()
    {
        var result = _workspace.ResolvePath("../../../etc/passwd");

        result.Should().BeNull();
    }

    [Fact]
    public void ResolvePath_AbsolutePath_ReturnsNull()
    {
        var result = _workspace.ResolvePath("/etc/passwd");

        result.Should().BeNull();
    }

    [Fact]
    public void ResolvePath_WindowsAbsolutePath_ReturnsNull()
    {
        var result = _workspace.ResolvePath("C:\\Windows\\System32");

        result.Should().BeNull();
    }

    [Fact]
    public void ResolvePath_EmptyPath_ReturnsNull()
    {
        var result = _workspace.ResolvePath("");

        result.Should().BeNull();
    }

    [Fact]
    public void ResolvePath_NullPath_ReturnsNull()
    {
        var result = _workspace.ResolvePath(null!);

        result.Should().BeNull();
    }

    [Fact]
    public void IsPathSafe_PathUnderRoot_ReturnsTrue()
    {
        var path = Path.Combine(_testRoot, "subfolder", "file.txt");

        var result = _workspace.IsPathSafe(path);

        result.Should().BeTrue();
    }

    [Fact]
    public void IsPathSafe_PathOutsideRoot_ReturnsFalse()
    {
        var path = Path.Combine(Path.GetTempPath(), "other_folder", "file.txt");

        var result = _workspace.IsPathSafe(path);

        result.Should().BeFalse();
    }

    [Fact]
    public void ResolvePath_NestedTraversal_ReturnsNull()
    {
        var result = _workspace.ResolvePath("foo/../../bar");

        // This should resolve to a path outside the root
        result.Should().BeNull();
    }

    [Fact]
    public void ResolvePath_ValidNestedPath_ReturnsFullPath()
    {
        var result = _workspace.ResolvePath("src/components/Button.tsx");

        result.Should().NotBeNull();
        result.Should().Contain("src");
        result.Should().Contain("components");
        result.Should().EndWith("Button.tsx");
    }

    [Fact]
    public void ResolvePath_SymlinkedParentOutsideRoot_ReturnsNull()
    {
        var outsideRoot = Path.Combine(Path.GetTempPath(), "agent_outside_" + Guid.NewGuid());
        Directory.CreateDirectory(outsideRoot);

        var symlinkPath = Path.Combine(_testRoot, "linked");
        try
        {
            Directory.CreateSymbolicLink(symlinkPath, outsideRoot);
        }
        catch
        {
            // Symlinks may be restricted; skip if not supported.
            Directory.Delete(outsideRoot, recursive: true);
            return;
        }

        try
        {
            var outsideFile = Path.Combine(outsideRoot, "file.txt");
            File.WriteAllText(outsideFile, "outside");

            var result = _workspace.ResolvePath("linked/file.txt");

            result.Should().BeNull();
        }
        finally
        {
            try
            {
                Directory.Delete(outsideRoot, recursive: true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
}

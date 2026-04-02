using System.Text.Json;
using Agent.Core.Modes;
using Agent.Core.Tools;
using FluentAssertions;

namespace Agent.Core.Tests;

public class ExecutionModeTests
{
    private static readonly ITool ReadFileTool = new FakeTool("ReadFile", new ToolPolicy { IsReadOnly = true });
    private static readonly ITool ListFilesTool = new FakeTool("ListFiles", new ToolPolicy { IsReadOnly = true });
    private static readonly ITool SearchTextTool = new FakeTool("SearchText", new ToolPolicy { IsReadOnly = true });
    private static readonly ITool GitStatusTool = new FakeTool("GitStatus", new ToolPolicy { IsReadOnly = true });
    private static readonly ITool FileEditTool = new FakeTool("FileEdit", new ToolPolicy { RequiresApproval = true, Risk = RiskLevel.Medium });
    private static readonly ITool RunCommandTool = new FakeTool("RunCommand", new ToolPolicy { RequiresApproval = true, Risk = RiskLevel.High });
    private static readonly ITool WorkNotesTool = new FakeTool("WorkNotes", new ToolPolicy { IsReadOnly = false });
    private static readonly ITool ApplyPatchTool = new FakeTool("ApplyPatch", new ToolPolicy { RequiresApproval = true, Risk = RiskLevel.Medium });

    private static readonly ITool[] AllTools =
    {
        ReadFileTool, ListFilesTool, SearchTextTool, GitStatusTool,
        FileEditTool, RunCommandTool, WorkNotesTool, ApplyPatchTool
    };

    [Fact]
    public void PlanMode_AllowsReadOnlyAndWorkNotes()
    {
        var mode = new PlanMode();

        mode.ToolFilter(ReadFileTool).Should().BeTrue();
        mode.ToolFilter(ListFilesTool).Should().BeTrue();
        mode.ToolFilter(SearchTextTool).Should().BeTrue();
        mode.ToolFilter(GitStatusTool).Should().BeTrue();
        mode.ToolFilter(WorkNotesTool).Should().BeTrue();
    }

    [Fact]
    public void PlanMode_BlocksWriteTools()
    {
        var mode = new PlanMode();

        mode.ToolFilter(FileEditTool).Should().BeFalse();
        mode.ToolFilter(RunCommandTool).Should().BeFalse();
        mode.ToolFilter(ApplyPatchTool).Should().BeFalse();
    }

    [Fact]
    public void ReviewMode_AllowsReadOnlyAndWorkNotes()
    {
        var mode = new ReviewMode();

        mode.ToolFilter(ReadFileTool).Should().BeTrue();
        mode.ToolFilter(GitStatusTool).Should().BeTrue();
        mode.ToolFilter(WorkNotesTool).Should().BeTrue();
    }

    [Fact]
    public void ReviewMode_BlocksWriteTools()
    {
        var mode = new ReviewMode();

        mode.ToolFilter(FileEditTool).Should().BeFalse();
        mode.ToolFilter(RunCommandTool).Should().BeFalse();
    }

    [Fact]
    public void ImplementMode_AllowsAllTools()
    {
        var mode = new ImplementMode();

        foreach (var tool in AllTools)
        {
            mode.ToolFilter(tool).Should().BeTrue($"{tool.Name} should be allowed in implement mode");
        }
    }

    [Fact]
    public void VerifyMode_AllowsReadOnlyAndRunCommandAndWorkNotes()
    {
        var mode = new VerifyMode();

        mode.ToolFilter(ReadFileTool).Should().BeTrue();
        mode.ToolFilter(SearchTextTool).Should().BeTrue();
        mode.ToolFilter(RunCommandTool).Should().BeTrue();
        mode.ToolFilter(WorkNotesTool).Should().BeTrue();
    }

    [Fact]
    public void VerifyMode_BlocksFileModificationTools()
    {
        var mode = new VerifyMode();

        mode.ToolFilter(FileEditTool).Should().BeFalse();
        mode.ToolFilter(ApplyPatchTool).Should().BeFalse();
    }

    [Fact]
    public void ExecutionModeRegistry_ResolvesAllBuiltInModes()
    {
        var registry = new ExecutionModeRegistry();

        registry.GetMode("plan").Should().NotBeNull().And.BeOfType<PlanMode>();
        registry.GetMode("review").Should().NotBeNull().And.BeOfType<ReviewMode>();
        registry.GetMode("implement").Should().NotBeNull().And.BeOfType<ImplementMode>();
        registry.GetMode("verify").Should().NotBeNull().And.BeOfType<VerifyMode>();
    }

    [Fact]
    public void ExecutionModeRegistry_IsCaseInsensitive()
    {
        var registry = new ExecutionModeRegistry();

        registry.GetMode("Plan").Should().NotBeNull();
        registry.GetMode("REVIEW").Should().NotBeNull();
        registry.GetMode("Implement").Should().NotBeNull();
    }

    [Fact]
    public void ExecutionModeRegistry_ReturnsNullForUnknown()
    {
        var registry = new ExecutionModeRegistry();

        registry.GetMode("nonexistent").Should().BeNull();
    }

    [Fact]
    public void ExecutionModeRegistry_GetModeNames_ReturnsAll()
    {
        var registry = new ExecutionModeRegistry();

        var names = registry.GetModeNames();
        names.Should().HaveCount(4);
        names.Should().Contain("plan");
        names.Should().Contain("review");
        names.Should().Contain("implement");
        names.Should().Contain("verify");
    }

    [Fact]
    public void AllModes_HaveNonEmptyPromptFragment()
    {
        var registry = new ExecutionModeRegistry();

        foreach (var mode in registry.GetAllModes())
        {
            mode.PromptFragment.Should().NotBeNullOrWhiteSpace($"{mode.Name} should have a prompt fragment");
        }
    }

    private class FakeTool : ITool
    {
        public FakeTool(string name, ToolPolicy policy) { Name = name; Policy = policy; }
        public string Name { get; }
        public string Description => "";
        public string InputSchema => "{}";
        public ToolPolicy Policy { get; }
        public Task<ToolResult> ExecuteAsync(JsonElement args, ToolContext context, CancellationToken ct = default)
            => Task.FromResult(ToolResult.Success(new { }));
    }
}

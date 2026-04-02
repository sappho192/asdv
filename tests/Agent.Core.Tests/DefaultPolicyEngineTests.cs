using System.Text.Json;
using Agent.Core.Policy;
using Agent.Core.Tools;
using FluentAssertions;

namespace Agent.Core.Tests;

public class DefaultPolicyEngineTests
{
    [Fact]
    public async Task AutoApprove_AllowsMediumRisk()
    {
        var engine = new DefaultPolicyEngine(new PolicyOptions { AutoApprove = true });
        var tool = new FakeTool("FileEdit", new ToolPolicy { RequiresApproval = true, Risk = RiskLevel.Medium });

        var decision = await engine.EvaluateAsync(tool, "{}");

        decision.Should().Be(PolicyDecision.Allowed);
    }

    [Fact]
    public async Task AutoApprove_StillRequiresApprovalForHighRisk()
    {
        var engine = new DefaultPolicyEngine(new PolicyOptions { AutoApprove = true });
        var tool = new FakeTool("RunCommand", new ToolPolicy { RequiresApproval = true, Risk = RiskLevel.High });

        var decision = await engine.EvaluateAsync(tool, "{}");

        decision.Should().Be(PolicyDecision.RequiresApproval);
    }

    [Fact]
    public void SetAutoApprove_TogglesState()
    {
        var engine = new DefaultPolicyEngine(new PolicyOptions { AutoApprove = false });

        engine.AutoApprove.Should().BeFalse();

        engine.SetAutoApprove(true);
        engine.AutoApprove.Should().BeTrue();

        engine.SetAutoApprove(false);
        engine.AutoApprove.Should().BeFalse();
    }

    [Fact]
    public async Task NoAutoApprove_ReadOnlyTool_Allowed()
    {
        var engine = new DefaultPolicyEngine(new PolicyOptions { AutoApprove = false });
        var tool = new FakeTool("ReadFile", new ToolPolicy { IsReadOnly = true });

        var decision = await engine.EvaluateAsync(tool, "{}");

        decision.Should().Be(PolicyDecision.Allowed);
    }

    [Fact]
    public async Task NoAutoApprove_ApprovalRequiredTool_RequiresApproval()
    {
        var engine = new DefaultPolicyEngine(new PolicyOptions { AutoApprove = false });
        var tool = new FakeTool("FileEdit", new ToolPolicy { RequiresApproval = true, Risk = RiskLevel.Medium });

        var decision = await engine.EvaluateAsync(tool, "{}");

        decision.Should().Be(PolicyDecision.RequiresApproval);
    }

    private class FakeTool : ITool
    {
        public FakeTool(string name, ToolPolicy policy)
        {
            Name = name;
            Policy = policy;
        }

        public string Name { get; }
        public string Description => "";
        public string InputSchema => "{}";
        public ToolPolicy Policy { get; }

        public Task<ToolResult> ExecuteAsync(JsonElement args, ToolContext context, CancellationToken ct = default)
            => Task.FromResult(ToolResult.Success(new { }));
    }
}

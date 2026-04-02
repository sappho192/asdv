using Agent.Core.Workflows;
using FluentAssertions;

namespace Agent.Core.Tests;

public class WorkflowTests : IDisposable
{
    private readonly string _tempDir;

    public WorkflowTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"asdv-wf-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void LoadFromDirectory_EmptyDir_ReturnsEmptyList()
    {
        var workflows = WorkflowLoader.LoadFromDirectory(_tempDir);
        workflows.Should().BeEmpty();
    }

    [Fact]
    public void LoadFromDirectory_NonExistentDir_ReturnsEmptyList()
    {
        var workflows = WorkflowLoader.LoadFromDirectory(Path.Combine(_tempDir, "nonexistent"));
        workflows.Should().BeEmpty();
    }

    [Fact]
    public void LoadFromDirectory_ValidYaml_ParsesWorkflow()
    {
        var yaml = """
            name: feature
            description: Plan, implement, and verify a feature
            steps:
              - mode: plan
                prompt: "Create a detailed implementation plan."
                maxIterations: 10
              - mode: implement
                maxIterations: 30
              - mode: verify
                prompt: "Run tests and verify correctness."
                maxIterations: 10
            """;

        File.WriteAllText(Path.Combine(_tempDir, "feature.yaml"), yaml);

        var workflows = WorkflowLoader.LoadFromDirectory(_tempDir);

        workflows.Should().HaveCount(1);
        var wf = workflows[0];
        wf.Name.Should().Be("feature");
        wf.Description.Should().Be("Plan, implement, and verify a feature");
        wf.Steps.Should().HaveCount(3);
        wf.Steps[0].Mode.Should().Be("plan");
        wf.Steps[0].Prompt.Should().Be("Create a detailed implementation plan.");
        wf.Steps[0].MaxIterations.Should().Be(10);
        wf.Steps[1].Mode.Should().Be("implement");
        wf.Steps[1].Prompt.Should().BeNull();
        wf.Steps[1].MaxIterations.Should().Be(30);
        wf.Steps[2].Mode.Should().Be("verify");
    }

    [Fact]
    public void LoadFromDirectory_MultipleFiles_LoadsAll()
    {
        File.WriteAllText(Path.Combine(_tempDir, "a.yaml"), "name: alpha\nsteps:\n  - mode: plan");
        File.WriteAllText(Path.Combine(_tempDir, "b.yml"), "name: beta\nsteps:\n  - mode: review");

        var workflows = WorkflowLoader.LoadFromDirectory(_tempDir);

        workflows.Should().HaveCount(2);
        workflows.Select(w => w.Name).Should().Contain("alpha").And.Contain("beta");
    }

    [Fact]
    public void LoadFromDirectory_InvalidYaml_SkipsGracefully()
    {
        File.WriteAllText(Path.Combine(_tempDir, "good.yaml"), "name: good\nsteps:\n  - mode: plan");
        File.WriteAllText(Path.Combine(_tempDir, "bad.yaml"), "{{invalid yaml content}}");

        var workflows = WorkflowLoader.LoadFromDirectory(_tempDir);

        // Should load at least the valid one
        workflows.Should().ContainSingle(w => w.Name == "good");
    }

    [Fact]
    public void LoadFromFile_ValidFile_ReturnsManifest()
    {
        var path = Path.Combine(_tempDir, "test.yaml");
        File.WriteAllText(path, "name: test\nsteps:\n  - mode: implement\n    maxIterations: 5");

        var wf = WorkflowLoader.LoadFromFile(path);

        wf.Should().NotBeNull();
        wf!.Name.Should().Be("test");
        wf.Steps.Should().HaveCount(1);
        wf.Steps[0].MaxIterations.Should().Be(5);
    }

    [Fact]
    public void LoadFromFile_NonExistent_ReturnsNull()
    {
        var wf = WorkflowLoader.LoadFromFile(Path.Combine(_tempDir, "nope.yaml"));
        wf.Should().BeNull();
    }

    [Fact]
    public void WorkflowManifest_DefaultSteps_IsEmptyList()
    {
        var manifest = new WorkflowManifest { Name = "empty" };
        manifest.Steps.Should().NotBeNull().And.BeEmpty();
    }
}

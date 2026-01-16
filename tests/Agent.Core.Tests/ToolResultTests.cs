using Agent.Core.Tools;
using FluentAssertions;

namespace Agent.Core.Tests;

public class ToolResultTests
{
    [Fact]
    public void Success_WithData_CreatesSuccessResult()
    {
        var data = new { message = "test" };

        var result = ToolResult.Success(data);

        result.Ok.Should().BeTrue();
        result.Data.Should().Be(data);
        result.Diagnostics.Should().BeNull();
    }

    [Fact]
    public void Success_WithStdout_CreatesSuccessResult()
    {
        var result = ToolResult.Success(stdout: "output text");

        result.Ok.Should().BeTrue();
        result.Stdout.Should().Be("output text");
    }

    [Fact]
    public void Failure_WithMessage_CreatesFailureResult()
    {
        var result = ToolResult.Failure("Something went wrong");

        result.Ok.Should().BeFalse();
        result.Diagnostics.Should().HaveCount(1);
        result.Diagnostics![0].Message.Should().Be("Something went wrong");
        result.Diagnostics[0].Code.Should().Be("Error");
    }

    [Fact]
    public void Failure_WithStderr_IncludesStderr()
    {
        var result = ToolResult.Failure("Error", stderr: "detailed error output");

        result.Ok.Should().BeFalse();
        result.Stderr.Should().Be("detailed error output");
    }
}

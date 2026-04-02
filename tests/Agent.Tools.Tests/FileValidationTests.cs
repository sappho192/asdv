using Agent.Core.Approval;
using Agent.Core.Tools;
using Agent.Workspace;
using FluentAssertions;
using Moq;

namespace Agent.Tools.Tests;

public class FileValidationTests : IDisposable
{
    private readonly string _testRoot;
    private readonly ToolContext _context;

    public FileValidationTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "agent_validation_test_" + Guid.NewGuid());
        Directory.CreateDirectory(_testRoot);
        _context = new ToolContext
        {
            RepoRoot = _testRoot,
            Workspace = new LocalWorkspace(_testRoot),
            ApprovalService = Mock.Of<IApprovalService>()
        };
    }

    public void Dispose()
    {
        try { Directory.Delete(_testRoot, recursive: true); } catch { }
    }

    [Fact]
    public async Task ValidateFile_ValidJson_ReturnsNull()
    {
        var file = Path.Combine(_testRoot, "valid.json");
        await File.WriteAllTextAsync(file, """{"key": "value"}""");

        var result = await FileValidation.ValidateFileAsync(file, _context, CancellationToken.None);
        result.Should().BeNull();
    }

    [Fact]
    public async Task ValidateFile_InvalidJson_ReturnsDiagnostic()
    {
        var file = Path.Combine(_testRoot, "invalid.json");
        await File.WriteAllTextAsync(file, "{broken json");

        var result = await FileValidation.ValidateFileAsync(file, _context, CancellationToken.None);
        result.Should().NotBeNull();
        result!.Code.Should().Be("ValidationWarning");
        result.Message.Should().Contain("JSON syntax error");
    }

    [Fact]
    public async Task ValidateFile_ValidXml_ReturnsNull()
    {
        var file = Path.Combine(_testRoot, "valid.xml");
        await File.WriteAllTextAsync(file, "<root><item>test</item></root>");

        var result = await FileValidation.ValidateFileAsync(file, _context, CancellationToken.None);
        result.Should().BeNull();
    }

    [Fact]
    public async Task ValidateFile_InvalidXml_ReturnsDiagnostic()
    {
        var file = Path.Combine(_testRoot, "invalid.xml");
        await File.WriteAllTextAsync(file, "<root><unclosed>");

        var result = await FileValidation.ValidateFileAsync(file, _context, CancellationToken.None);
        result.Should().NotBeNull();
        result!.Code.Should().Be("ValidationWarning");
        result.Message.Should().Contain("XML syntax error");
    }

    [Fact]
    public async Task ValidateFile_ValidCsproj_ReturnsNull()
    {
        var file = Path.Combine(_testRoot, "test.csproj");
        await File.WriteAllTextAsync(file, """<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup></PropertyGroup></Project>""");

        var result = await FileValidation.ValidateFileAsync(file, _context, CancellationToken.None);
        result.Should().BeNull();
    }

    [Fact]
    public async Task ValidateFile_UnknownExtension_ReturnsNull()
    {
        var file = Path.Combine(_testRoot, "readme.md");
        await File.WriteAllTextAsync(file, "# Hello");

        var result = await FileValidation.ValidateFileAsync(file, _context, CancellationToken.None);
        result.Should().BeNull();
    }

    [Fact]
    public async Task ValidateFile_JsWithoutNode_ReturnsNull()
    {
        FileValidation.SetEnvironment(hasNode: false, hasPython: false);
        var file = Path.Combine(_testRoot, "app.js");
        await File.WriteAllTextAsync(file, "const x = {{{broken");

        var result = await FileValidation.ValidateFileAsync(file, _context, CancellationToken.None);
        result.Should().BeNull(); // No node available, so no validation
    }
}

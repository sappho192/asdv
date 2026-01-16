namespace Agent.Core.Tools;

public sealed record ToolResult
{
    public bool Ok { get; init; }
    public string? Stdout { get; init; }
    public string? Stderr { get; init; }
    public object? Data { get; init; }
    public IReadOnlyList<Diagnostic>? Diagnostics { get; init; }

    public static ToolResult Success(object? data = null, string? stdout = null) =>
        new() { Ok = true, Data = data, Stdout = stdout };

    public static ToolResult Failure(string message, string? stderr = null) =>
        new() { Ok = false, Stderr = stderr, Diagnostics = [new Diagnostic("Error", message)] };
}

public sealed record Diagnostic(string Code, string Message, object? Details = null);

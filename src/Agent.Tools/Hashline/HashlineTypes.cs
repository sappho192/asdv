namespace Agent.Tools.Hashline;

public record LineRef(int Line, string Hash);

public abstract record HashlineEdit
{
    public record Replace(string Pos, string? End, string[] Lines) : HashlineEdit;
    public record Append(string? Pos, string[] Lines) : HashlineEdit;
    public record Prepend(string? Pos, string[] Lines) : HashlineEdit;
}

public record RawHashlineEdit
{
    public string? Op { get; init; }
    public string? Pos { get; init; }
    public string? End { get; init; }
    public object? Lines { get; init; } // string, string[], or null
}

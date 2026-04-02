namespace Agent.Tools.Hashline;

/// <summary>
/// Detects and preserves BOM and line endings during file editing.
/// </summary>
public record FileTextEnvelope(string Content, bool HadBom, string LineEnding);

public static class FileTextCanonicalization
{
    public static FileTextEnvelope Canonicalize(string content)
    {
        bool hadBom = content.StartsWith('\uFEFF');
        var stripped = hadBom ? content[1..] : content;
        var lineEnding = DetectLineEnding(stripped);
        var normalized = stripped.Replace("\r\n", "\n").Replace("\r", "\n");
        return new FileTextEnvelope(normalized, hadBom, lineEnding);
    }

    public static string Restore(string content, FileTextEnvelope envelope)
    {
        var withLineEnding = envelope.LineEnding == "\r\n"
            ? content.Replace("\n", "\r\n")
            : content;
        return envelope.HadBom ? $"\uFEFF{withLineEnding}" : withLineEnding;
    }

    private static string DetectLineEnding(string content)
    {
        var crlfIndex = content.IndexOf("\r\n", StringComparison.Ordinal);
        var lfIndex = content.IndexOf('\n');
        if (lfIndex == -1) return "\n";
        if (crlfIndex == -1) return "\n";
        return crlfIndex < lfIndex ? "\r\n" : "\n";
    }
}

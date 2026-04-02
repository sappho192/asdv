using System.Text;
using System.Text.RegularExpressions;

namespace Agent.Tools.Hashline;

public static partial class HashlineValidation
{
    private const string ValidChars = "ZPMQVRWSNKTXJBYH";
    private const int MismatchContext = 2;

    [GeneratedRegex(@"^(\d+)#([ZPMQVRWSNKTXJBYH]{2})$")]
    private static partial Regex HashlineRefPattern();

    [GeneratedRegex(@"(\d+#[ZPMQVRWSNKTXJBYH]{2})")]
    private static partial Regex HashlineRefExtractPattern();

    public static string NormalizeLineRef(string refStr)
    {
        var trimmed = refStr.Trim();
        // Strip leading markers
        if (trimmed.StartsWith(">>>"))
            trimmed = trimmed[3..].TrimStart();
        else if (trimmed.StartsWith(">>"))
            trimmed = trimmed[2..].TrimStart();
        else if (trimmed.Length > 0 && (trimmed[0] == '+' || trimmed[0] == '-') && !trimmed.StartsWith("++") && !trimmed.StartsWith("--"))
            trimmed = trimmed[1..].TrimStart();

        // Normalize spacing around #
        trimmed = trimmed.Replace(" #", "#").Replace("# ", "#");

        // Strip content after |
        var pipeIdx = trimmed.IndexOf('|');
        if (pipeIdx >= 0)
            trimmed = trimmed[..pipeIdx];

        trimmed = trimmed.Trim();

        if (HashlineRefPattern().IsMatch(trimmed))
            return trimmed;

        // Try to extract from within the string
        var match = HashlineRefExtractPattern().Match(trimmed);
        if (match.Success)
            return match.Groups[1].Value;

        return refStr.Trim();
    }

    public static LineRef ParseLineRef(string refStr)
    {
        var normalized = NormalizeLineRef(refStr);
        var match = HashlineRefPattern().Match(normalized);
        if (match.Success)
        {
            return new LineRef(int.Parse(match.Groups[1].Value), match.Groups[2].Value);
        }

        var hashIdx = normalized.IndexOf('#');
        if (hashIdx > 0)
        {
            var prefix = normalized[..hashIdx];
            var suffix = normalized[(hashIdx + 1)..];
            if (!int.TryParse(prefix, out _) && Regex.IsMatch(suffix, $"^[{ValidChars}]{{2}}$"))
            {
                throw new InvalidOperationException(
                    $"Invalid line reference: \"{refStr}\". \"{prefix}\" is not a line number. " +
                    "Use the actual line number from the read output.");
            }
        }

        throw new InvalidOperationException(
            $"Invalid line reference format: \"{refStr}\". Expected format: \"{{line_number}}#{{hash_id}}\"");
    }

    public static void ValidateLineRef(string[] lines, string refStr)
    {
        var lineRef = ParseLineRef(refStr);

        if (lineRef.Line < 1 || lineRef.Line > lines.Length)
        {
            throw new InvalidOperationException(
                $"Line number {lineRef.Line} out of bounds. File has {lines.Length} lines.");
        }

        var content = lines[lineRef.Line - 1];
        if (HashlineComputation.ComputeLineHash(lineRef.Line, content) != lineRef.Hash)
        {
            throw new HashlineMismatchException(
                [(lineRef.Line, lineRef.Hash)], lines);
        }
    }

    public static void ValidateLineRefs(string[] lines, IReadOnlyList<string> refs)
    {
        var mismatches = new List<(int Line, string Expected)>();

        foreach (var refStr in refs)
        {
            var lineRef = ParseLineRef(refStr);

            if (lineRef.Line < 1 || lineRef.Line > lines.Length)
            {
                throw new InvalidOperationException(
                    $"Line number {lineRef.Line} out of bounds (file has {lines.Length} lines)");
            }

            var content = lines[lineRef.Line - 1];
            if (HashlineComputation.ComputeLineHash(lineRef.Line, content) != lineRef.Hash)
            {
                mismatches.Add((lineRef.Line, lineRef.Hash));
            }
        }

        if (mismatches.Count > 0)
        {
            throw new HashlineMismatchException(mismatches, lines);
        }
    }
}

public class HashlineMismatchException : Exception
{
    public IReadOnlyDictionary<string, string> Remaps { get; }

    public HashlineMismatchException(
        IReadOnlyList<(int Line, string Expected)> mismatches,
        string[] fileLines)
        : base(FormatMessage(mismatches, fileLines))
    {
        var remaps = new Dictionary<string, string>();
        foreach (var (line, expected) in mismatches)
        {
            var actual = HashlineComputation.ComputeLineHash(line, fileLines[line - 1]);
            remaps[$"{line}#{expected}"] = $"{line}#{actual}";
        }
        Remaps = remaps;
    }

    private static string FormatMessage(
        IReadOnlyList<(int Line, string Expected)> mismatches,
        string[] fileLines)
    {
        var mismatchByLine = new Dictionary<int, string>();
        foreach (var (line, expected) in mismatches)
            mismatchByLine[line] = expected;

        var displayLines = new SortedSet<int>();
        foreach (var (line, _) in mismatches)
        {
            var low = Math.Max(1, line - 2);
            var high = Math.Min(fileLines.Length, line + 2);
            for (int i = low; i <= high; i++)
                displayLines.Add(i);
        }

        var sb = new StringBuilder();
        sb.AppendLine(
            $"{mismatches.Count} line{(mismatches.Count > 1 ? "s have" : " has")} changed since last read. " +
            "Use updated {line_number}#{hash_id} references below (>>> marks changed lines).");
        sb.AppendLine();

        int previousLine = -1;
        foreach (var line in displayLines)
        {
            if (previousLine != -1 && line > previousLine + 1)
                sb.AppendLine("    ...");
            previousLine = line;

            var content = fileLines[line - 1];
            var hash = HashlineComputation.ComputeLineHash(line, content);
            var prefix = $"{line}#{hash}|{content}";
            sb.AppendLine(mismatchByLine.ContainsKey(line) ? $">>> {prefix}" : $"    {prefix}");
        }

        return sb.ToString();
    }
}

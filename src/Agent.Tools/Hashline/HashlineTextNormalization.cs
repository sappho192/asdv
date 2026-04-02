using System.Text.RegularExpressions;

namespace Agent.Tools.Hashline;

/// <summary>
/// Normalizes text input from models: strips hashline prefixes, diff markers, and boundary echoes.
/// </summary>
public static partial class HashlineTextNormalization
{
    [GeneratedRegex(@"^\s*(?:>>>|>>)?\s*\d+\s*#\s*[ZPMQVRWSNKTXJBYH]{2}\|")]
    private static partial Regex HashlinePrefixPattern();

    [GeneratedRegex(@"^[+](?![+])")]
    private static partial Regex DiffPlusPattern();

    private static bool EqualsIgnoringWhitespace(string a, string b)
    {
        if (a == b) return true;
        return Regex.Replace(a, @"\s+", "") == Regex.Replace(b, @"\s+", "");
    }

    /// <summary>
    /// Strip hashline prefixes or diff + markers from lines if they appear in ≥50% of non-empty lines.
    /// </summary>
    public static string[] StripLinePrefixes(string[] lines)
    {
        int hashPrefixCount = 0, diffPlusCount = 0, nonEmpty = 0;

        foreach (var line in lines)
        {
            if (line.Length == 0) continue;
            nonEmpty++;
            if (HashlinePrefixPattern().IsMatch(line)) hashPrefixCount++;
            if (DiffPlusPattern().IsMatch(line)) diffPlusCount++;
        }

        if (nonEmpty == 0) return lines;

        bool stripHash = hashPrefixCount > 0 && hashPrefixCount >= nonEmpty * 0.5;
        bool stripPlus = !stripHash && diffPlusCount > 0 && diffPlusCount >= nonEmpty * 0.5;

        if (!stripHash && !stripPlus) return lines;

        return lines.Select(line =>
        {
            if (stripHash) return HashlinePrefixPattern().Replace(line, "");
            if (stripPlus) return DiffPlusPattern().Replace(line, "");
            return line;
        }).ToArray();
    }

    /// <summary>
    /// Convert string or string[] input to normalized lines array.
    /// </summary>
    public static string[] ToNewLines(object? input)
    {
        if (input is string[] arr)
            return StripLinePrefixes(arr);
        if (input is string str)
            return StripLinePrefixes(str.Split('\n'));
        return [];
    }

    /// <summary>
    /// Restore leading indent from template line if replacement has none.
    /// </summary>
    public static string RestoreLeadingIndent(string templateLine, string line)
    {
        if (line.Length == 0) return line;
        var templateIndent = GetLeadingWhitespace(templateLine);
        if (templateIndent.Length == 0) return line;
        if (GetLeadingWhitespace(line).Length > 0) return line;
        if (templateLine.Trim() == line.Trim()) return line;
        return templateIndent + line;
    }

    /// <summary>
    /// Strip anchor echo: if first inserted line matches the anchor line, remove it.
    /// </summary>
    public static string[] StripInsertAnchorEcho(string anchorLine, string[] newLines)
    {
        if (newLines.Length == 0) return newLines;
        if (EqualsIgnoringWhitespace(newLines[0], anchorLine))
            return newLines[1..];
        return newLines;
    }

    /// <summary>
    /// Strip before echo: if last inserted line matches the anchor line, remove it.
    /// </summary>
    public static string[] StripInsertBeforeEcho(string anchorLine, string[] newLines)
    {
        if (newLines.Length <= 1) return newLines;
        if (EqualsIgnoringWhitespace(newLines[^1], anchorLine))
            return newLines[..^1];
        return newLines;
    }

    /// <summary>
    /// Strip boundary echoes from range replacement: remove context lines that match before/after the range.
    /// </summary>
    public static string[] StripRangeBoundaryEcho(string[] lines, int startLine, int endLine, string[] newLines)
    {
        var replacedCount = endLine - startLine + 1;
        if (newLines.Length <= 1 || newLines.Length <= replacedCount)
            return newLines;

        var result = newLines;
        var beforeIdx = startLine - 2;
        if (beforeIdx >= 0 && EqualsIgnoringWhitespace(result[0], lines[beforeIdx]))
            result = result[1..];

        var afterIdx = endLine;
        if (afterIdx < lines.Length && result.Length > 0 && EqualsIgnoringWhitespace(result[^1], lines[afterIdx]))
            result = result[..^1];

        return result;
    }

    private static string GetLeadingWhitespace(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        int i = 0;
        while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
        return text[..i];
    }
}

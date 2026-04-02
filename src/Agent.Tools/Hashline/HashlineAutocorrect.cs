using System.Text.RegularExpressions;

namespace Agent.Tools.Hashline;

/// <summary>
/// Autocorrects replacement lines: restores wrapped lines, expands single-line merges, restores indentation.
/// </summary>
public static partial class HashlineAutocorrect
{
    [GeneratedRegex(@"(?:&&|\|\||\?\?|\?|:|=|,|\+|-|\*|/|\.|\()\s*$")]
    private static partial Regex TrailingContinuationPattern();

    public static string[] AutocorrectReplacementLines(string[] originalLines, string[] replacementLines)
    {
        var next = replacementLines;
        next = MaybeExpandSingleLineMerge(originalLines, next);
        next = RestoreOldWrappedLines(originalLines, next);
        next = RestoreIndentForPairedReplacement(originalLines, next);
        return next;
    }

    /// <summary>
    /// If the model merged multiple original lines into one, try to expand back.
    /// </summary>
    public static string[] MaybeExpandSingleLineMerge(string[] originalLines, string[] replacementLines)
    {
        if (replacementLines.Length != 1 || originalLines.Length <= 1)
            return replacementLines;

        var merged = replacementLines[0];
        var parts = originalLines.Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
        if (parts.Length != originalLines.Length) return replacementLines;

        // Try ordered substring matching
        var indices = new List<int>();
        int offset = 0;
        bool orderedMatch = true;

        foreach (var part in parts)
        {
            int idx = merged.IndexOf(part, offset, StringComparison.Ordinal);
            int matchedLen = part.Length;

            if (idx == -1)
            {
                var stripped = TrailingContinuationPattern().Replace(part, "");
                if (stripped != part)
                {
                    idx = merged.IndexOf(stripped, offset, StringComparison.Ordinal);
                    if (idx != -1) matchedLen = stripped.Length;
                }
            }

            if (idx == -1)
            {
                orderedMatch = false;
                break;
            }

            indices.Add(idx);
            offset = idx + matchedLen;
        }

        if (orderedMatch && indices.Count == parts.Length)
        {
            var expanded = new List<string>();
            for (int i = 0; i < indices.Count; i++)
            {
                int start = indices[i];
                int end = i + 1 < indices.Count ? indices[i + 1] : merged.Length;
                var candidate = merged[start..end].Trim();
                if (candidate.Length == 0)
                {
                    orderedMatch = false;
                    break;
                }
                expanded.Add(candidate);
            }

            if (orderedMatch && expanded.Count == originalLines.Length)
                return expanded.ToArray();
        }

        // Fallback: try semicolon split
        var semicolonParts = merged.Split(';', StringSplitOptions.None)
            .Select((part, idx) =>
            {
                var trimmed = part.Trim();
                if (trimmed.Length == 0) return trimmed;
                // Re-add semicolons to all but possibly the last
                if (idx < merged.Split(';').Length - 1)
                    return trimmed.EndsWith(';') ? trimmed : trimmed + ";";
                return trimmed;
            })
            .Where(l => l.Length > 0)
            .ToArray();

        if (semicolonParts.Length == originalLines.Length)
            return semicolonParts;

        return replacementLines;
    }

    /// <summary>
    /// Detect and restore lines that the model split across multiple lines.
    /// </summary>
    public static string[] RestoreOldWrappedLines(string[] originalLines, string[] replacementLines)
    {
        if (originalLines.Length == 0 || replacementLines.Length < 2)
            return replacementLines;

        var canonicalToOriginal = new Dictionary<string, (string Line, int Count)>();
        foreach (var line in originalLines)
        {
            var canonical = Regex.Replace(line, @"\s+", "");
            if (canonicalToOriginal.TryGetValue(canonical, out var existing))
                canonicalToOriginal[canonical] = (existing.Line, existing.Count + 1);
            else
                canonicalToOriginal[canonical] = (line, 1);
        }

        var candidates = new List<(int Start, int Len, string Replacement, string Canonical)>();
        for (int start = 0; start < replacementLines.Length; start++)
        {
            for (int len = 2; len <= 10 && start + len <= replacementLines.Length; len++)
            {
                var span = replacementLines[start..(start + len)];
                if (span.Any(l => l.Trim().Length == 0)) continue;

                var canonicalSpan = Regex.Replace(string.Join("", span), @"\s+", "");
                if (canonicalToOriginal.TryGetValue(canonicalSpan, out var original) &&
                    original.Count == 1 && canonicalSpan.Length >= 6)
                {
                    candidates.Add((start, len, original.Line, canonicalSpan));
                }
            }
        }

        if (candidates.Count == 0) return replacementLines;

        // Only use candidates with unique canonical forms
        var canonicalCounts = new Dictionary<string, int>();
        foreach (var c in candidates)
        {
            canonicalCounts.TryGetValue(c.Canonical, out var count);
            canonicalCounts[c.Canonical] = count + 1;
        }

        var unique = candidates.Where(c => canonicalCounts[c.Canonical] == 1)
            .OrderByDescending(c => c.Start)
            .ToList();

        if (unique.Count == 0) return replacementLines;

        var result = replacementLines.ToList();
        foreach (var c in unique)
        {
            result.RemoveRange(c.Start, c.Len);
            result.Insert(c.Start, c.Replacement);
        }

        return result.ToArray();
    }

    /// <summary>
    /// If replacement has same count as original, restore indentation from original lines.
    /// </summary>
    public static string[] RestoreIndentForPairedReplacement(string[] originalLines, string[] replacementLines)
    {
        if (originalLines.Length != replacementLines.Length)
            return replacementLines;

        return replacementLines.Select((line, idx) =>
        {
            if (line.Length == 0) return line;
            if (GetLeadingWhitespace(line).Length > 0) return line;
            var indent = GetLeadingWhitespace(originalLines[idx]);
            if (indent.Length == 0) return line;
            if (originalLines[idx].Trim() == line.Trim()) return line;
            return indent + line;
        }).ToArray();
    }

    private static string GetLeadingWhitespace(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        int i = 0;
        while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
        return text[..i];
    }
}

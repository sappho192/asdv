namespace Agent.Tools.Hashline;

/// <summary>
/// Core edit operations: apply hashline edits to file content with validation, dedup, and overlap detection.
/// </summary>
public static class HashlineEditOperations
{
    public record ApplyReport(string Content, int NoopEdits, int DeduplicatedEdits);

    private static readonly Dictionary<string, int> EditPrecedence = new()
    {
        ["replace"] = 0, ["append"] = 1, ["prepend"] = 2
    };

    public static ApplyReport ApplyEditsWithReport(string content, HashlineEdit[] edits)
    {
        if (edits.Length == 0)
            return new ApplyReport(content, 0, 0);

        var (deduped, deduplicatedCount) = HashlineDeduplication.DedupeEdits(edits);

        // Sort bottom-up (highest line first) for safe in-place editing
        var sorted = deduped.OrderByDescending(e => GetEditLineNumber(e))
            .ThenBy(e => GetEditPrecedence(e))
            .ToArray();

        var lines = content.Length == 0 ? Array.Empty<string>() : content.Split('\n');

        // Validate all line refs
        var refs = CollectLineRefs(sorted);
        HashlineValidation.ValidateLineRefs(lines, refs);

        // Check for overlapping ranges
        var overlapError = DetectOverlappingRanges(sorted);
        if (overlapError != null)
            throw new InvalidOperationException(overlapError);

        int noopEdits = 0;
        var currentLines = lines.ToList();

        foreach (var edit in sorted)
        {
            var before = currentLines.ToArray();
            currentLines = ApplyEdit(currentLines, edit);

            if (ArraysEqual(before, currentLines.ToArray()))
                noopEdits++;
        }

        return new ApplyReport(string.Join('\n', currentLines), noopEdits, deduplicatedCount);
    }

    public static string ApplyEdits(string content, HashlineEdit[] edits)
    {
        return ApplyEditsWithReport(content, edits).Content;
    }

    private static List<string> ApplyEdit(List<string> lines, HashlineEdit edit)
    {
        return edit switch
        {
            HashlineEdit.Replace r => ApplyReplace(lines, r),
            HashlineEdit.Append a => ApplyAppend(lines, a),
            HashlineEdit.Prepend p => ApplyPrepend(lines, p),
            _ => lines
        };
    }

    private static List<string> ApplyReplace(List<string> lines, HashlineEdit.Replace edit)
    {
        if (edit.End != null)
            return ApplyReplaceRange(lines, edit.Pos, edit.End, edit.Lines);
        return ApplySetLine(lines, edit.Pos, edit.Lines);
    }

    private static List<string> ApplySetLine(List<string> lines, string anchor, string[] newText)
    {
        var lineRef = HashlineValidation.ParseLineRef(anchor);
        var result = new List<string>(lines);
        var originalLine = lines[lineRef.Line - 1];
        var normalized = HashlineTextNormalization.ToNewLines(newText);
        var corrected = HashlineAutocorrect.AutocorrectReplacementLines([originalLine], normalized);
        var replacement = corrected.Select((entry, idx) =>
            idx == 0 ? HashlineTextNormalization.RestoreLeadingIndent(originalLine, entry) : entry
        ).ToArray();

        result.RemoveAt(lineRef.Line - 1);
        result.InsertRange(lineRef.Line - 1, replacement);
        return result;
    }

    private static List<string> ApplyReplaceRange(List<string> lines, string startAnchor, string endAnchor, string[] newText)
    {
        var startRef = HashlineValidation.ParseLineRef(startAnchor);
        var endRef = HashlineValidation.ParseLineRef(endAnchor);

        if (startRef.Line > endRef.Line)
            throw new InvalidOperationException(
                $"Invalid range: start line {startRef.Line} cannot be greater than end line {endRef.Line}");

        var result = new List<string>(lines);
        var originalRange = lines.Skip(startRef.Line - 1).Take(endRef.Line - startRef.Line + 1).ToArray();
        var normalized = HashlineTextNormalization.ToNewLines(newText);
        var stripped = HashlineTextNormalization.StripRangeBoundaryEcho(
            lines.ToArray(), startRef.Line, endRef.Line, normalized);
        var corrected = HashlineAutocorrect.AutocorrectReplacementLines(originalRange, stripped);
        var restored = corrected.Select((entry, idx) =>
            idx == 0 ? HashlineTextNormalization.RestoreLeadingIndent(lines[startRef.Line - 1], entry) : entry
        ).ToArray();

        result.RemoveRange(startRef.Line - 1, endRef.Line - startRef.Line + 1);
        result.InsertRange(startRef.Line - 1, restored);
        return result;
    }

    private static List<string> ApplyAppend(List<string> lines, HashlineEdit.Append edit)
    {
        var normalized = HashlineTextNormalization.ToNewLines(edit.Lines);
        if (normalized.Length == 0)
            throw new InvalidOperationException("append requires non-empty text");

        if (edit.Pos != null)
        {
            var lineRef = HashlineValidation.ParseLineRef(edit.Pos);
            var stripped = HashlineTextNormalization.StripInsertAnchorEcho(lines[lineRef.Line - 1], normalized);
            if (stripped.Length == 0)
                throw new InvalidOperationException($"append (anchored) requires non-empty text for {edit.Pos}");
            var result = new List<string>(lines);
            result.InsertRange(lineRef.Line, stripped);
            return result;
        }

        // Append to end
        if (lines.Count == 1 && lines[0] == "")
            return new List<string>(normalized);
        var appended = new List<string>(lines);
        appended.AddRange(normalized);
        return appended;
    }

    private static List<string> ApplyPrepend(List<string> lines, HashlineEdit.Prepend edit)
    {
        var normalized = HashlineTextNormalization.ToNewLines(edit.Lines);
        if (normalized.Length == 0)
            throw new InvalidOperationException("prepend requires non-empty text");

        if (edit.Pos != null)
        {
            var lineRef = HashlineValidation.ParseLineRef(edit.Pos);
            var stripped = HashlineTextNormalization.StripInsertBeforeEcho(lines[lineRef.Line - 1], normalized);
            if (stripped.Length == 0)
                throw new InvalidOperationException($"prepend (anchored) requires non-empty text for {edit.Pos}");
            var result = new List<string>(lines);
            result.InsertRange(lineRef.Line - 1, stripped);
            return result;
        }

        // Prepend to start
        if (lines.Count == 1 && lines[0] == "")
            return new List<string>(normalized);
        var prepended = new List<string>(normalized);
        prepended.AddRange(lines);
        return prepended;
    }

    // --- Helpers ---

    private static int GetEditLineNumber(HashlineEdit edit)
    {
        return edit switch
        {
            HashlineEdit.Replace r => HashlineValidation.ParseLineRef(r.End ?? r.Pos).Line,
            HashlineEdit.Append a => a.Pos != null ? HashlineValidation.ParseLineRef(a.Pos).Line : int.MinValue,
            HashlineEdit.Prepend p => p.Pos != null ? HashlineValidation.ParseLineRef(p.Pos).Line : int.MinValue,
            _ => int.MaxValue
        };
    }

    private static int GetEditPrecedence(HashlineEdit edit)
    {
        var key = edit switch
        {
            HashlineEdit.Replace => "replace",
            HashlineEdit.Append => "append",
            HashlineEdit.Prepend => "prepend",
            _ => ""
        };
        return EditPrecedence.GetValueOrDefault(key, 3);
    }

    public static List<string> CollectLineRefs(HashlineEdit[] edits)
    {
        var refs = new List<string>();
        foreach (var edit in edits)
        {
            switch (edit)
            {
                case HashlineEdit.Replace r:
                    refs.Add(r.Pos);
                    if (r.End != null) refs.Add(r.End);
                    break;
                case HashlineEdit.Append a when a.Pos != null:
                    refs.Add(a.Pos);
                    break;
                case HashlineEdit.Prepend p when p.Pos != null:
                    refs.Add(p.Pos);
                    break;
            }
        }
        return refs;
    }

    public static string? DetectOverlappingRanges(HashlineEdit[] edits)
    {
        // Collect all range replacements
        var ranges = new List<(int Start, int End, int Idx)>();
        for (int i = 0; i < edits.Length; i++)
        {
            if (edits[i] is not HashlineEdit.Replace { End: not null } r) continue;
            var start = HashlineValidation.ParseLineRef(r.Pos).Line;
            var end = HashlineValidation.ParseLineRef(r.End).Line;
            ranges.Add((start, end, i));
        }

        // Check range-vs-range overlaps
        if (ranges.Count >= 2)
        {
            ranges.Sort((a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : a.End.CompareTo(b.End));
            for (int i = 1; i < ranges.Count; i++)
            {
                if (ranges[i].Start <= ranges[i - 1].End)
                {
                    return $"Overlapping range edits detected: " +
                        $"edit {ranges[i - 1].Idx + 1} (lines {ranges[i - 1].Start}-{ranges[i - 1].End}) overlaps with " +
                        $"edit {ranges[i].Idx + 1} (lines {ranges[i].Start}-{ranges[i].End}). " +
                        $"Use pos-only replace for single-line edits.";
                }
            }
        }

        // Check that non-range edits (single replace, append, prepend) don't target lines inside a range
        if (ranges.Count > 0)
        {
            for (int i = 0; i < edits.Length; i++)
            {
                int? targetLine = edits[i] switch
                {
                    HashlineEdit.Replace { End: null } r => HashlineValidation.ParseLineRef(r.Pos).Line,
                    HashlineEdit.Append { Pos: not null } a => HashlineValidation.ParseLineRef(a.Pos).Line,
                    HashlineEdit.Prepend { Pos: not null } p => HashlineValidation.ParseLineRef(p.Pos).Line,
                    _ => null
                };

                if (targetLine == null) continue;

                foreach (var range in ranges)
                {
                    if (range.Idx == i) continue; // skip self
                    if (targetLine >= range.Start && targetLine <= range.End)
                    {
                        return $"Edit {i + 1} targets line {targetLine} which falls inside range edit {range.Idx + 1} " +
                            $"(lines {range.Start}-{range.End}). " +
                            $"Combine these into a single range replacement, or edit outside the range.";
                    }
                }
            }
        }

        return null;
    }

    private static bool ArraysEqual(string[] a, string[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
            if (a[i] != b[i]) return false;
        return true;
    }
}

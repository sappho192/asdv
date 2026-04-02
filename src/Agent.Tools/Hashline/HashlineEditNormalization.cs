using System.Text.Json;

namespace Agent.Tools.Hashline;

/// <summary>
/// Normalizes raw JSON edit input into typed HashlineEdit objects.
/// </summary>
public static class HashlineEditNormalization
{
    public static HashlineEdit[] NormalizeEdits(JsonElement editsArray)
    {
        var result = new List<HashlineEdit>();
        int index = 0;

        foreach (var rawEdit in editsArray.EnumerateArray())
        {
            var op = rawEdit.TryGetProperty("op", out var opEl) ? opEl.GetString() : null;
            var pos = rawEdit.TryGetProperty("pos", out var posEl) ? NormalizeAnchor(posEl.GetString()) : null;
            var end = rawEdit.TryGetProperty("end", out var endEl) ? NormalizeAnchor(endEl.GetString()) : null;

            string[] lines;
            if (rawEdit.TryGetProperty("lines", out var linesEl))
            {
                if (linesEl.ValueKind == JsonValueKind.Null)
                    lines = [];
                else if (linesEl.ValueKind == JsonValueKind.Array)
                    lines = linesEl.EnumerateArray().Select(e => e.GetString() ?? "").ToArray();
                else if (linesEl.ValueKind == JsonValueKind.String)
                    lines = linesEl.GetString()?.Split('\n') ?? [];
                else
                    lines = [];
            }
            else
            {
                throw new InvalidOperationException($"Edit {index}: lines is required");
            }

            var edit = op switch
            {
                "replace" => NormalizeReplace(pos, end, lines, index),
                "append" => NormalizeAppend(pos, end, lines, index),
                "prepend" => NormalizePrepend(pos, end, lines, index),
                _ => throw new InvalidOperationException(
                    $"Edit {index}: unsupported op \"{op}\". Use replace, append, or prepend.")
            };

            result.Add(edit);
            index++;
        }

        return result.ToArray();
    }

    private static HashlineEdit NormalizeReplace(string? pos, string? end, string[] lines, int index)
    {
        var anchor = pos ?? end ?? throw new InvalidOperationException(
            $"Edit {index}: replace requires at least one anchor line reference (pos or end)");
        return new HashlineEdit.Replace(anchor, end, lines);
    }

    private static HashlineEdit NormalizeAppend(string? pos, string? end, string[] lines, int index)
    {
        var anchor = pos ?? end;
        return new HashlineEdit.Append(anchor, lines);
    }

    private static HashlineEdit NormalizePrepend(string? pos, string? end, string[] lines, int index)
    {
        var anchor = pos ?? end;
        return new HashlineEdit.Prepend(anchor, lines);
    }

    private static string? NormalizeAnchor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return value.Trim();
    }
}

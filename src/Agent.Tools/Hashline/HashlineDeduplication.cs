namespace Agent.Tools.Hashline;

public static class HashlineDeduplication
{
    public static (HashlineEdit[] Edits, int DeduplicatedCount) DedupeEdits(HashlineEdit[] edits)
    {
        var seen = new HashSet<string>();
        var deduped = new List<HashlineEdit>();
        int deduplicatedCount = 0;

        foreach (var edit in edits)
        {
            var key = BuildDedupeKey(edit);
            if (seen.Contains(key))
            {
                deduplicatedCount++;
                continue;
            }
            seen.Add(key);
            deduped.Add(edit);
        }

        return (deduped.ToArray(), deduplicatedCount);
    }

    private static string BuildDedupeKey(HashlineEdit edit)
    {
        return edit switch
        {
            HashlineEdit.Replace r =>
                $"replace|{Canonical(r.Pos)}|{(r.End != null ? Canonical(r.End) : "")}|{string.Join("\n", r.Lines)}",
            HashlineEdit.Append a =>
                $"append|{Canonical(a.Pos)}|{string.Join("\n", a.Lines)}",
            HashlineEdit.Prepend p =>
                $"prepend|{Canonical(p.Pos)}|{string.Join("\n", p.Lines)}",
            _ => edit.ToString() ?? ""
        };
    }

    private static string Canonical(string? anchor)
    {
        if (string.IsNullOrEmpty(anchor)) return "";
        return HashlineValidation.NormalizeLineRef(anchor);
    }
}

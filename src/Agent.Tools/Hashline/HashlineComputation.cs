using System.IO.Hashing;
using System.Text;

namespace Agent.Tools.Hashline;

/// <summary>
/// Computes 2-character content-hash IDs (CIDs) for file lines using xxHash32.
/// Compatible with oh-my-openagent's HASHLINE_DICT character set.
/// </summary>
public static class HashlineComputation
{
    private const string NibbleStr = "ZPMQVRWSNKTXJBYH";

    private static readonly string[] Dict = BuildDict();

    // Matches any Unicode letter or digit
    private static bool HasSignificantChar(string text)
    {
        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch))
                return true;
        }
        return false;
    }

    private static string[] BuildDict()
    {
        var dict = new string[256];
        for (int i = 0; i < 256; i++)
        {
            var high = i >>> 4;
            var low = i & 0x0F;
            dict[i] = $"{NibbleStr[high]}{NibbleStr[low]}";
        }
        return dict;
    }

    private static string ComputeNormalizedHash(int lineNumber, string normalizedContent)
    {
        // Seed: 0 if line has alphanumeric content, lineNumber otherwise
        uint seed = HasSignificantChar(normalizedContent) ? 0u : (uint)lineNumber;
        var bytes = Encoding.UTF8.GetBytes(normalizedContent);
        var hash = XxHash32.HashToUInt32(bytes, (int)seed);
        var index = (int)(hash % 256);
        return Dict[index];
    }

    /// <summary>
    /// Compute the 2-char hash for a line. Content is normalized by removing \r and trimming trailing whitespace.
    /// </summary>
    public static string ComputeLineHash(int lineNumber, string content)
    {
        var normalized = content.Replace("\r", "").TrimEnd();
        return ComputeNormalizedHash(lineNumber, normalized);
    }

    /// <summary>
    /// Format a line as hashline output: "LINE#HASH|content"
    /// </summary>
    public static string FormatHashLine(int lineNumber, string content)
    {
        var hash = ComputeLineHash(lineNumber, content);
        return $"{lineNumber}#{hash}|{content}";
    }

    /// <summary>
    /// Format entire file content as hashline output.
    /// </summary>
    public static string FormatHashLines(string content)
    {
        if (string.IsNullOrEmpty(content))
            return "";

        var lines = content.Split('\n');
        var sb = new StringBuilder();
        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0) sb.Append('\n');
            sb.Append(FormatHashLine(i + 1, lines[i]));
        }
        return sb.ToString();
    }
}

using System.Text;

namespace Agent.Cli.Rendering;

public sealed class TextStreamFormatter
{
    private bool _inCodeBlock;
    private int _backtickRun;
    private bool _pendingBackslash;

    public string Format(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var output = new StringBuilder(text.Length);

        foreach (var ch in text)
        {
            if (_pendingBackslash)
            {
                if (ch == 'n' && !_inCodeBlock)
                {
                    output.AppendLine();
                }
                else
                {
                    output.Append('\\');
                    ProcessChar(ch, output);
                }

                _pendingBackslash = false;
                continue;
            }

            if (ch == '\\')
            {
                _pendingBackslash = true;
                continue;
            }

            ProcessChar(ch, output);
        }

        return output.ToString();
    }

    public string Flush()
    {
        var output = new StringBuilder();

        if (_pendingBackslash)
        {
            output.Append('\\');
            _pendingBackslash = false;
        }

        FlushBackticks(output);
        return output.ToString();
    }

    private void ProcessChar(char ch, StringBuilder output)
    {
        if (ch == '`')
        {
            _backtickRun++;
            return;
        }

        FlushBackticks(output);
        output.Append(ch);
    }

    private void FlushBackticks(StringBuilder output)
    {
        if (_backtickRun == 0)
        {
            return;
        }

        var remaining = _backtickRun;
        while (remaining >= 3)
        {
            _inCodeBlock = !_inCodeBlock;
            output.Append("```");
            remaining -= 3;
        }

        if (remaining > 0)
        {
            output.Append('`', remaining);
        }

        _backtickRun = 0;
    }
}

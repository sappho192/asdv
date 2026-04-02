using Agent.Core.Events;
using Agent.Core.Tools;

namespace Agent.Cli.Rendering;

public sealed class ConsoleEventRenderer
{
    private readonly TextStreamFormatter _formatter = new();

    public void Render(AgentEvent evt)
    {
        switch (evt)
        {
            case TextDelta delta:
                Console.Write(_formatter.Format(delta.Text));
                break;

            case ToolCallStarted started:
                Console.Write(_formatter.Flush());
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write($"[tool] {started.ToolName}");
                Console.ResetColor();
                break;

            case ToolCallReady ready:
                Console.WriteLine($" args={Truncate(ready.ArgsJson, 100)}");
                break;

            case TraceEvent trace when trace.Kind == "error":
                Console.Write(_formatter.Flush());
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[Provider error] {trace.Data}");
                Console.ResetColor();
                break;

            case ResponseCompleted:
                Console.Write(_formatter.Flush());
                Console.WriteLine();
                break;

            case ToolExecutionCompleted completed:
                PrintToolResult(completed.ToolName, completed.Result);
                break;

            case SessionStarted session:
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"[Session {session.SessionId} | {session.Provider}/{session.Model}{(session.Resumed ? " (resumed)" : "")}]");
                Console.ResetColor();
                break;

            case SessionCompleted sc:
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"[Session completed: {sc.Reason} ({sc.TotalIterations} iterations)]");
                Console.ResetColor();
                break;

            case SessionError se:
                // Already handled by AgentError/MaxIterationsReached — skip duplicate output
                break;

            case WorkflowStepStarted stepStart:
                Console.ForegroundColor = ConsoleColor.Magenta;
                Console.WriteLine($"[Workflow '{stepStart.WorkflowName}' — Step {stepStart.StepIndex + 1}: {stepStart.ModeName}]");
                Console.ResetColor();
                break;

            case WorkflowStepCompleted stepEnd:
                Console.ForegroundColor = ConsoleColor.Magenta;
                Console.WriteLine($"[Step {stepEnd.StepIndex + 1} completed: {stepEnd.Reason}]");
                Console.ResetColor();
                break;

            case ToolProgressEvent progress:
                Console.ForegroundColor = ConsoleColor.DarkGray;
                var pctStr = progress.PercentComplete.HasValue ? $" ({progress.PercentComplete:F0}%)" : "";
                Console.WriteLine($"  [{progress.ToolName}] {progress.Message}{pctStr}");
                Console.ResetColor();
                break;

            case AgentCompleted:
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("[Agent completed]");
                Console.ResetColor();
                break;

            case AgentError error:
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[{error.Message}]");
                Console.ResetColor();
                break;

            case MaxIterationsReached:
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("[Max iterations reached]");
                Console.ResetColor();
                break;
        }
    }

    private static void PrintToolResult(string toolName, ToolResult result)
    {
        if (result.Ok)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  [{toolName}] OK");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  [{toolName}] FAILED: {result.Diagnostics?.FirstOrDefault()?.Message}");
        }
        Console.ResetColor();

        if (!string.IsNullOrEmpty(result.Stdout))
        {
            var preview = TruncateMultiline(result.Stdout, 200);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"  stdout: {preview}");
            Console.ResetColor();
        }
    }

    private static string Truncate(string text, int maxLength)
    {
        if (text.Length <= maxLength) return text;
        return text[..maxLength] + "...";
    }

    private static string TruncateMultiline(string str, int maxLength)
    {
        var singleLine = str.Replace("\r", "").Replace("\n", " ");
        if (singleLine.Length <= maxLength) return singleLine;
        return singleLine[..maxLength] + "...";
    }
}

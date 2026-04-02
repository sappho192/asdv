using Agent.Core.Tools;

namespace Agent.Core.Modes;

public sealed class VerifyMode : IExecutionMode
{
    public string Name => "verify";

    public string PromptFragment => """
        You are in **verification mode**. Run tests and verify the implementation is correct.
        - Use read-only tools to inspect code
        - Use RunCommand to execute tests and build commands
        - Do NOT make code changes — only verify
        - Use WorkNotes to record test results and issues found
        """;

    public Func<ITool, bool> ToolFilter => tool =>
        tool.Policy.IsReadOnly || tool.Name == "RunCommand" || tool.Name == "WorkNotes";
}

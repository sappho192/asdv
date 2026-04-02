using Agent.Core.Tools;

namespace Agent.Core.Modes;

public sealed class ReviewMode : IExecutionMode
{
    public string Name => "review";

    public string PromptFragment => """
        You are in **review mode**. Examine the codebase and recent changes, then provide feedback.
        - Use read-only and git tools to inspect code
        - Do NOT make any changes to files
        - Use WorkNotes to store your review findings
        - Focus on correctness, code quality, and potential issues
        """;

    public Func<ITool, bool> ToolFilter => tool =>
        tool.Policy.IsReadOnly || tool.Name == "WorkNotes";
}

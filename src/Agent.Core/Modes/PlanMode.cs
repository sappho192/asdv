using Agent.Core.Tools;

namespace Agent.Core.Modes;

public sealed class PlanMode : IExecutionMode
{
    public string Name => "plan";

    public string PromptFragment => """
        You are in **planning mode**. Your task is to analyze the codebase and create a detailed implementation plan.
        - Do NOT make any changes to files
        - Use read-only tools (ReadFile, ListFiles, SearchText, GitStatus, GitDiff) to explore
        - Use WorkNotes to store your plan with key 'plan'
        - Include specific file paths, function names, and change descriptions in your plan
        """;

    public Func<ITool, bool> ToolFilter => tool => tool.Policy.IsReadOnly || tool.Name == "WorkNotes";
}

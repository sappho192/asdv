using Agent.Core.Tools;

namespace Agent.Core.Modes;

public sealed class ImplementMode : IExecutionMode
{
    public string Name => "implement";

    public string PromptFragment => """
        You are in **implementation mode**. Execute the plan precisely and make changes.
        - All tools are available
        - Read WorkNotes for the current plan before starting
        - Update WorkNotes with progress as you complete steps
        - Verify changes with git status/diff after modifications
        """;

    public Func<ITool, bool> ToolFilter => _ => true;
}

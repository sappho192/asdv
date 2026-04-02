using Agent.Core.Tools;

namespace Agent.Core.Modes;

public interface IExecutionMode
{
    string Name { get; }
    string PromptFragment { get; }
    Func<ITool, bool> ToolFilter { get; }
}

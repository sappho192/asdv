using Agent.Core.Providers;

namespace Agent.Core.Tools;

public class ToolRegistry
{
    private readonly Dictionary<string, ITool> _tools = new(StringComparer.OrdinalIgnoreCase);

    public void Register(ITool tool)
    {
        _tools[tool.Name] = tool;
    }

    public ITool? GetTool(string name)
    {
        return _tools.TryGetValue(name, out var tool) ? tool : null;
    }

    public IEnumerable<ITool> GetAllTools() => _tools.Values;

    public IReadOnlyList<ToolDefinition> GetToolDefinitions()
    {
        return _tools.Values.Select(t => new ToolDefinition(t.Name, t.Description, t.InputSchema)).ToList();
    }

    public string GetToolDescriptionsMarkdown()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var tool in _tools.Values)
        {
            var readOnly = tool.Policy.IsReadOnly ? " (read-only)" : "";
            var approval = tool.Policy.RequiresApproval ? " [requires approval]" : "";
            sb.AppendLine($"- **{tool.Name}**: {tool.Description}{readOnly}{approval}");
        }
        return sb.ToString();
    }
}

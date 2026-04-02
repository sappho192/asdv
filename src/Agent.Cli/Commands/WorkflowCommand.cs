using Agent.Core.Workflows;

namespace Agent.Cli.Commands;

public class WorkflowCommand : ICommand
{
    private readonly List<WorkflowManifest> _workflows;

    public WorkflowCommand(List<WorkflowManifest> workflows)
    {
        _workflows = workflows;
    }

    public string Name => "workflow";
    public string Description => "Manage workflows: /workflow list, /workflow run <name> [prompt]";

    public Task ExecuteAsync(string[] args, CommandContext context)
    {
        var action = args.Length > 0 ? args[0].ToLowerInvariant() : "list";

        switch (action)
        {
            case "list":
                if (_workflows.Count == 0)
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine("No workflows found. Add .yaml files to .asdv/workflows/");
                    Console.ResetColor();
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"Workflows ({_workflows.Count}):");
                    Console.ResetColor();
                    foreach (var wf in _workflows)
                    {
                        var desc = wf.Description != null ? $" — {wf.Description}" : "";
                        var steps = string.Join(" → ", wf.Steps.Select(s => s.Mode));
                        Console.WriteLine($"  {wf.Name}{desc}");
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.WriteLine($"    Steps: {steps}");
                        Console.ResetColor();
                    }
                }
                break;

            case "run":
                if (args.Length < 2)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("Usage: /workflow run <name> [prompt]");
                    Console.ResetColor();
                }
                else
                {
                    var name = args[1];
                    var wf = _workflows.FirstOrDefault(w =>
                        string.Equals(w.Name, name, StringComparison.OrdinalIgnoreCase));

                    if (wf == null)
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"Workflow '{name}' not found.");
                        Console.ResetColor();
                    }
                    else
                    {
                        // Store workflow request — actual execution handled by REPL loop
                        context.OnWorkflowRequested?.Invoke(wf, args.Length > 2 ? string.Join(" ", args[2..]) : null);
                    }
                }
                break;

            default:
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Usage: /workflow [list|run]");
                Console.ResetColor();
                break;
        }

        return Task.CompletedTask;
    }
}

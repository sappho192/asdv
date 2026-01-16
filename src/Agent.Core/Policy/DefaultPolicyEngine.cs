using System.Text.Json;
using Agent.Core.Tools;

namespace Agent.Core.Policy;

public class DefaultPolicyEngine : IPolicyEngine
{
    private readonly PolicyOptions _options;

    public DefaultPolicyEngine(PolicyOptions options)
    {
        _options = options;
    }

    public Task<PolicyDecision> EvaluateAsync(ITool tool, string argsJson)
    {
        if (_options.AutoApprove)
        {
            return Task.FromResult(PolicyDecision.Allowed);
        }

        if (tool.Policy.RequiresApproval)
        {
            return Task.FromResult(PolicyDecision.RequiresApproval);
        }

        if (tool.Name.Equals("RunCommand", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var args = JsonDocument.Parse(argsJson).RootElement;
                if (IsDangerousCommand(args))
                {
                    return Task.FromResult(PolicyDecision.RequiresApproval);
                }
            }
            catch
            {
                return Task.FromResult(PolicyDecision.RequiresApproval);
            }
        }

        return Task.FromResult(PolicyDecision.Allowed);
    }

    private static bool IsDangerousCommand(JsonElement args)
    {
        if (!args.TryGetProperty("exe", out var exe)) return false;

        var command = exe.GetString()?.ToLowerInvariant() ?? "";
        var dangerous = new[] { "rm", "del", "rmdir", "format", "curl", "wget", "ssh", "powershell", "cmd", "bash", "sh" };

        return dangerous.Any(d => command.Contains(d, StringComparison.OrdinalIgnoreCase));
    }
}

public record PolicyOptions
{
    public bool AutoApprove { get; init; }
}

using Agent.Core.Tools;

namespace Agent.Core.Policy;

public interface IPolicyEngine
{
    Task<PolicyDecision> EvaluateAsync(ITool tool, string argsJson);
}

public enum PolicyDecision
{
    Allowed,
    RequiresApproval,
    Denied
}

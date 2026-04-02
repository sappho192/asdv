using System.Runtime.CompilerServices;
using Agent.Core.Approval;
using Agent.Core.Events;
using Agent.Core.Logging;
using Agent.Core.Messages;
using Agent.Core.Modes;
using Agent.Core.Orchestrator;
using Agent.Core.Policy;
using Agent.Core.Providers;
using Agent.Core.Session;
using Agent.Core.Tools;

namespace Agent.Core.Workflows;

public sealed class WorkflowRunner
{
    private readonly IModelProvider _provider;
    private readonly ToolRegistry _toolRegistry;
    private readonly IApprovalService _approvalService;
    private readonly IPolicyEngine _policyEngine;
    private readonly ISessionLogger _logger;
    private readonly ExecutionModeRegistry _modeRegistry;

    public WorkflowRunner(
        IModelProvider provider,
        ToolRegistry toolRegistry,
        IApprovalService approvalService,
        IPolicyEngine policyEngine,
        ISessionLogger logger,
        ExecutionModeRegistry modeRegistry)
    {
        _provider = provider;
        _toolRegistry = toolRegistry;
        _approvalService = approvalService;
        _policyEngine = policyEngine;
        _logger = logger;
        _modeRegistry = modeRegistry;
    }

    public async IAsyncEnumerable<AgentEvent> RunWorkflowAsync(
        WorkflowManifest workflow,
        string initialPrompt,
        AgentOptions baseOptions,
        List<ChatMessage> messages,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        for (int stepIdx = 0; stepIdx < workflow.Steps.Count; stepIdx++)
        {
            var step = workflow.Steps[stepIdx];
            var mode = _modeRegistry.GetMode(step.Mode);

            // Validate mode exists
            if (mode == null)
            {
                yield return new WorkflowStepStarted(workflow.Name, stepIdx, step.Mode);
                yield return new WorkflowStepCompleted(workflow.Name, stepIdx,
                    $"failed: unknown mode '{step.Mode}'");
                yield break;
            }

            yield return new WorkflowStepStarted(workflow.Name, stepIdx, step.Mode);

            // Compact messages between steps (except first step)
            if (stepIdx > 0 && baseOptions.State?.MaxContextTokens != null)
            {
                var budget = ContextCompactor.GetTargetBudget(baseOptions.State.MaxContextTokens.Value);
                var compacted = ContextCompactor.CompactSlidingWindow(messages, budget);
                messages.Clear();
                messages.AddRange(compacted);
            }

            // Build step-specific options
            var stepOptions = baseOptions with
            {
                Mode = mode,
                MaxIterations = step.MaxIterations ?? baseOptions.MaxIterations
            };

            // Determine prompt for this step
            var stepPrompt = stepIdx == 0
                ? (step.Prompt != null ? $"{step.Prompt}\n\n{initialPrompt}" : initialPrompt)
                : (step.Prompt ?? $"Continue with the {step.Mode} step of the '{workflow.Name}' workflow.");

            var orchestrator = new AgentOrchestrator(
                _provider, _toolRegistry, _approvalService, _policyEngine, _logger, stepOptions);

            // Track terminal events to detect step failure
            var stepFailed = false;
            string? failReason = null;

            await foreach (var evt in orchestrator.RunStreamAsync(stepPrompt, messages, ct))
            {
                yield return evt;

                switch (evt)
                {
                    case AgentError e:
                        stepFailed = true;
                        failReason = e.Message;
                        break;
                    case MaxIterationsReached m:
                        stepFailed = true;
                        failReason = $"max iterations reached ({m.Iterations})";
                        break;
                }
            }

            if (stepFailed)
            {
                yield return new WorkflowStepCompleted(workflow.Name, stepIdx, $"failed: {failReason}");
                yield break;
            }

            yield return new WorkflowStepCompleted(workflow.Name, stepIdx, "completed");
        }
    }
}

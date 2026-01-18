using System.Collections.Concurrent;
using System.Threading.Channels;
using Agent.Core.Approval;
using Agent.Server.Models;

namespace Agent.Server.Services;

public sealed class ServerApprovalService : IApprovalService
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _pending = new();
    private ChannelWriter<ServerEvent>? _writer;

    public void AttachChannel(ChannelWriter<ServerEvent> writer)
    {
        _writer = writer;
    }

    public Task<bool> RequestApprovalAsync(
        string toolName,
        string argsJson,
        string? callId = null,
        CancellationToken ct = default)
    {
        var approvalId = string.IsNullOrWhiteSpace(callId)
            ? $"approval_{Guid.NewGuid():n}"
            : callId;

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[approvalId] = tcs;

        _writer?.TryWrite(new ApprovalRequiredEvent(
            approvalId,
            toolName,
            argsJson,
            "RequiresApproval"));

        if (ct.CanBeCanceled)
        {
            ct.Register(() => tcs.TrySetCanceled(ct));
        }

        return tcs.Task;
    }

    public bool TryResolve(string callId, bool approved)
    {
        if (_pending.TryRemove(callId, out var tcs))
        {
            return tcs.TrySetResult(approved);
        }

        return false;
    }
}

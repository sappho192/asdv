namespace Agent.Core.Approval;

public class ConsoleApprovalService : IApprovalService
{
    public Task<bool> RequestApprovalAsync(string toolName, string argsJson, CancellationToken ct = default)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"[Approval Required] Tool: {toolName}");
        Console.ResetColor();
        Console.WriteLine($"Args: {argsJson}");
        Console.Write("Approve? (y/N): ");

        var input = Console.ReadLine();
        return Task.FromResult(
            input?.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) == true
        );
    }
}

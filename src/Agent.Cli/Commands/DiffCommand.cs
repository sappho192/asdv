using System.Diagnostics;

namespace Agent.Cli.Commands;

public class DiffCommand : ICommand
{
    public string Name => "diff";
    public string Description => "Show git diff summary";

    public async Task ExecuteAsync(string[] args, CommandContext context)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = context.RepoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("diff");
        psi.ArgumentList.Add("--stat");

        try
        {
            using var process = Process.Start(psi)!;
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (string.IsNullOrWhiteSpace(output))
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("No unstaged changes.");
                Console.ResetColor();
            }
            else
            {
                Console.WriteLine(output.TrimEnd());
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Failed to run git diff: {ex.Message}");
            Console.ResetColor();
        }
    }
}

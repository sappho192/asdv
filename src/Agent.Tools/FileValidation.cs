using System.Diagnostics;
using System.Text.Json;
using System.Xml;
using Agent.Core.Tools;

namespace Agent.Tools;

/// <summary>
/// Post-edit file validation: validates JSON, XML natively; JS/Python via external runtimes when available.
/// </summary>
public static class FileValidation
{
    private static bool? _hasNode;
    private static bool? _hasPython;

    /// <summary>
    /// Initialize runtime availability from environment detection.
    /// </summary>
    public static void SetEnvironment(bool hasNode, bool hasPython)
    {
        _hasNode = hasNode;
        _hasPython = hasPython;
    }

    /// <summary>
    /// Validate a file after editing. Returns a Diagnostic if validation fails, null if OK or no validator available.
    /// </summary>
    public static async Task<Diagnostic?> ValidateFileAsync(string fullPath, ToolContext ctx, CancellationToken ct)
    {
        var ext = Path.GetExtension(fullPath).ToLowerInvariant();

        return ext switch
        {
            ".json" => ValidateJson(fullPath),
            ".xml" or ".csproj" or ".props" or ".targets" or ".config" => ValidateXml(fullPath),
            ".js" or ".mjs" or ".cjs" => _hasNode == true ? await ValidateNodeAsync(fullPath, ct) : null,
            ".ts" or ".tsx" or ".jsx" => null, // TypeScript needs tsc, skip
            ".py" => _hasPython == true ? await ValidatePythonAsync(fullPath, ct) : null,
            _ => null
        };
    }

    private static Diagnostic? ValidateJson(string fullPath)
    {
        try
        {
            var content = File.ReadAllText(fullPath);
            JsonDocument.Parse(content);
            return null;
        }
        catch (JsonException ex)
        {
            return new Diagnostic("ValidationWarning",
                $"JSON syntax error: {ex.Message}. The file may be malformed.");
        }
    }

    private static Diagnostic? ValidateXml(string fullPath)
    {
        try
        {
            var doc = new XmlDocument();
            doc.Load(fullPath);
            return null;
        }
        catch (XmlException ex)
        {
            return new Diagnostic("ValidationWarning",
                $"XML syntax error: {ex.Message}. The file may be malformed.");
        }
    }

    private static async Task<Diagnostic?> ValidateNodeAsync(string fullPath, CancellationToken ct)
    {
        return await RunValidationCommandAsync("node", ["--check", fullPath], ct);
    }

    private static async Task<Diagnostic?> ValidatePythonAsync(string fullPath, CancellationToken ct)
    {
        var pythonCmd = OperatingSystem.IsWindows() ? "python" : "python3";
        var script = $"import ast; ast.parse(open(r'{fullPath}').read())";
        return await RunValidationCommandAsync(pythonCmd, ["-c", script], ct);
    }

    private static async Task<Diagnostic?> RunValidationCommandAsync(
        string command, string[] args, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = command,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (var arg in args)
                psi.ArgumentList.Add(arg);

            using var process = Process.Start(psi);
            if (process == null) return null;

            var stderr = await process.StandardError.ReadToEndAsync(ct);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(10));

            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return null; // Timeout is not a validation failure
            }

            if (process.ExitCode != 0)
            {
                var msg = string.IsNullOrWhiteSpace(stderr) ? "Syntax check failed" : stderr.Trim();
                if (msg.Length > 500) msg = msg[..500] + "...";
                return new Diagnostic("ValidationWarning", $"Syntax error: {msg}");
            }

            return null;
        }
        catch
        {
            return null; // Don't fail edits due to validation issues
        }
    }
}

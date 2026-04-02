using Agent.Core.Tools;

namespace Agent.Server.Services;

public static class SystemPromptProvider
{
    public static string GetSystemPrompt(ToolRegistry toolRegistry, string repoRoot)
    {
        var toolDescriptions = toolRegistry.GetToolDescriptionsMarkdown();

        var projectPromptPath = Path.Combine(repoRoot, ".asdv", "prompt.md");
        var projectPrompt = File.Exists(projectPromptPath)
            ? Environment.NewLine + File.ReadAllText(projectPromptPath)
            : "";

        return $"""
            You are a coding assistant that helps developers with tasks in their local repository.

            ## Available Tools

            {toolDescriptions}
            ## Guidelines

            1. **Understand First**: Always read relevant files before making changes
            2. **Search Effectively**: Use SearchText to locate code patterns
            3. **Precise Edits**: Use FileEdit for targeted string replacements, or ApplyPatch for larger changes
            4. **Verify Results**: Check git status/diff after modifications
            5. **Test Changes**: Run tests when appropriate
            6. **Explain Actions**: Briefly describe what you're doing and why
            7. **Track Progress**: Use WorkNotes to store plans, key findings, and TODOs

            ## Edit Strategies

            For small, targeted changes, prefer FileEdit (exact string replacement).
            For larger or multi-site changes, use ApplyPatch with unified diff format:
            ```
            --- a/path/to/file.cs
            +++ b/path/to/file.cs
            @@ -10,3 +10,4 @@
             context line
            -removed line
            +added line
             context line
            ```

            Keep changes minimal and focused on the specific task.
            {projectPrompt}
            """;
    }
}

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Agent.Core.Workflows;

public static class WorkflowLoader
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static List<WorkflowManifest> LoadFromDirectory(string workflowsDir)
    {
        var workflows = new List<WorkflowManifest>();

        if (!Directory.Exists(workflowsDir))
            return workflows;

        foreach (var file in Directory.GetFiles(workflowsDir, "*.yaml")
                     .Concat(Directory.GetFiles(workflowsDir, "*.yml")))
        {
            try
            {
                var yaml = File.ReadAllText(file);
                var manifest = Deserializer.Deserialize<WorkflowManifest>(yaml);
                if (manifest != null && !string.IsNullOrWhiteSpace(manifest.Name))
                {
                    workflows.Add(manifest);
                }
            }
            catch
            {
                // Skip invalid workflow files
            }
        }

        return workflows;
    }

    public static WorkflowManifest? LoadFromFile(string filePath)
    {
        if (!File.Exists(filePath))
            return null;

        var yaml = File.ReadAllText(filePath);
        return Deserializer.Deserialize<WorkflowManifest>(yaml);
    }
}

using YamlDotNet.RepresentationModel;

namespace Agent.Core.Config;

public static class AppConfigLoader
{
    public static AppConfig Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Config file not found: {path}");
        }

        using var reader = new StreamReader(path);
        var yaml = new YamlStream();
        yaml.Load(reader);

        if (yaml.Documents.Count == 0 || yaml.Documents[0].RootNode is not YamlMappingNode root)
        {
            return new AppConfig();
        }

        return new AppConfig
        {
            Provider = GetScalarValue(root, "provider"),
            Model = GetScalarValue(root, "model", "defaultModel"),
            OpenAICompatibleEndpoint = GetScalarValue(
                root,
                "openaiCompatibleEndpoint",
                "openai_compatible_endpoint",
                "openai-compatible-endpoint")
        };
    }

    public static AppConfig? LoadIfExists(string path)
    {
        return File.Exists(path) ? Load(path) : null;
    }

    private static string? GetScalarValue(YamlMappingNode root, params string[] keys)
    {
        foreach (var entry in root.Children)
        {
            if (entry.Key is not YamlScalarNode keyNode)
            {
                continue;
            }

            var key = keyNode.Value ?? string.Empty;
            if (!keys.Any(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (entry.Value is YamlScalarNode valueNode)
            {
                var value = valueNode.Value?.Trim();
                return string.IsNullOrEmpty(value) ? null : value;
            }

            return null;
        }

        return null;
    }
}

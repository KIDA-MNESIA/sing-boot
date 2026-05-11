using System.Collections;
using System.Web.Script.Serialization;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace SingBoot;

/// <summary>
/// Holds a validated core configuration and any stdin payload required to start it.
/// </summary>
public sealed class CoreConfig
{
    public string? StandardInputContent { get; }
    public bool RequiresElevation { get; }

    private CoreConfig(string? standardInputContent, bool requiresElevation)
    {
        StandardInputContent = standardInputContent;
        RequiresElevation = requiresElevation;
    }

    public static CoreConfig Load(CoreProfile profile)
    {
        return profile.Kind switch
        {
            CoreKind.SingBox => LoadSingBox(profile.ConfigPath),
            CoreKind.Mihomo => LoadMihomo(profile.ConfigPath),
            _ => throw new InvalidOperationException("Unsupported core kind.")
        };
    }

    private static CoreConfig LoadSingBox(string configPath)
    {
        if (!File.Exists(configPath))
            throw new FileNotFoundException("Configuration file not found.", configPath);

        var rawJson = File.ReadAllText(configPath, System.Text.Encoding.UTF8);
        var normalized = JsonHelper.NormalizeJson(rawJson);

        if (string.IsNullOrWhiteSpace(normalized))
            throw new InvalidOperationException("Configuration file is empty or contains invalid JSON.");

        return new CoreConfig(normalized, DetectSingBoxTunInbound(normalized));
    }

    private static CoreConfig LoadMihomo(string configPath)
    {
        if (!File.Exists(configPath))
            throw new FileNotFoundException("Configuration file not found.", configPath);

        var rawYaml = File.ReadAllText(configPath, System.Text.Encoding.UTF8);
        if (string.IsNullOrWhiteSpace(rawYaml))
            throw new InvalidOperationException("Configuration file is empty.");

        var yaml = new YamlStream();
        try
        {
            using var reader = new StringReader(rawYaml);
            yaml.Load(reader);
        }
        catch (YamlException ex)
        {
            throw new InvalidOperationException("Configuration file contains invalid YAML.", ex);
        }

        if (yaml.Documents.Count == 0 || yaml.Documents[0].RootNode is not YamlMappingNode root)
            throw new InvalidOperationException("Configuration file is empty or contains invalid YAML.");

        return new CoreConfig(null, DetectMihomoTun(root));
    }

    private static bool DetectSingBoxTunInbound(string normalizedJson)
    {
        object rootObject;
        try
        {
            rootObject = new JavaScriptSerializer().DeserializeObject(normalizedJson);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException("Configuration file is empty or contains invalid JSON.", ex);
        }

        var root = rootObject as IDictionary;
        if (root is null || !root.Contains("inbounds"))
            return false;

        var inbounds = root["inbounds"] as IEnumerable;
        if (inbounds is null)
            return false;

        foreach (var inboundObj in inbounds)
        {
            var inbound = inboundObj as IDictionary;
            if (inbound is null || !inbound.Contains("type"))
                continue;

            var typeValue = inbound["type"] as string;
            if (string.Equals(typeValue, "tun", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool DetectMihomoTun(YamlMappingNode root)
    {
        if (TryGetMappingValue(root, "tun", out var tunNode) &&
            tunNode is YamlMappingNode tun &&
            TryGetMappingValue(tun, "enable", out var enableNode) &&
            IsTruthyScalar(enableNode))
        {
            return true;
        }

        if (!TryGetMappingValue(root, "listeners", out var listenersNode) ||
            listenersNode is not YamlSequenceNode listeners)
        {
            return false;
        }

        foreach (var listenerNode in listeners.Children)
        {
            if (listenerNode is not YamlMappingNode listener)
                continue;

            if (TryGetMappingValue(listener, "type", out var typeNode) &&
                typeNode is YamlScalarNode typeScalar &&
                string.Equals(typeScalar.Value, "tun", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetMappingValue(YamlMappingNode mapping, string key, out YamlNode value)
    {
        foreach (var child in mapping.Children)
        {
            if (child.Key is YamlScalarNode scalar &&
                string.Equals(scalar.Value, key, StringComparison.OrdinalIgnoreCase))
            {
                value = child.Value;
                return true;
            }
        }

        value = null!;
        return false;
    }

    private static bool IsTruthyScalar(YamlNode node)
    {
        if (node is not YamlScalarNode scalar || scalar.Value is null)
            return false;

        return scalar.Value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
               scalar.Value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
               scalar.Value.Equals("on", StringComparison.OrdinalIgnoreCase) ||
               scalar.Value.Equals("1", StringComparison.OrdinalIgnoreCase);
    }
}

using System.Collections;
using System.Globalization;
using System.Web.Script.Serialization;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace SingBoot;

/// <summary>
/// Holds core configuration details and any stdin payload required to start it.
/// </summary>
public sealed class CoreConfig
{
    public string? StandardInputContent { get; }
    public bool RequiresElevation { get; }

    static CoreConfig()
    {
        EmbeddedAssemblyResolver.Initialize();
    }

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

        return new CoreConfig(null, DetectMihomoTun(rawYaml));
    }

    private static bool DetectSingBoxTunInbound(string normalizedJson)
    {
        object rootObject;
        try
        {
            rootObject = new JavaScriptSerializer().DeserializeObject(normalizedJson);
        }
        catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
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

    private static bool DetectMihomoTun(string rawYaml)
    {
        object? rootObject;
        try
        {
            using var reader = new StringReader(rawYaml);
            var parser = new MergingParser(new Parser(reader));
            rootObject = new DeserializerBuilder().Build().Deserialize<object>(parser);
        }
        catch (YamlException ex)
        {
            throw new InvalidOperationException("Configuration file contains invalid YAML.", ex);
        }

        if (rootObject is not IDictionary root)
            throw new InvalidOperationException("Configuration file is empty or does not contain a YAML mapping.");

        if (TryGetMappingValue(root, "tun", out var tunObject) &&
            tunObject is IDictionary tun &&
            TryGetMappingValue(tun, "enable", out var enableValue) &&
            IsTruthyScalar(enableValue))
        {
            return true;
        }

        if (!TryGetMappingValue(root, "listeners", out var listenersObject) ||
            listenersObject is string ||
            listenersObject is not IEnumerable listeners)
        {
            return false;
        }

        foreach (var listenerObject in listeners)
        {
            if (listenerObject is IDictionary listener &&
                TryGetMappingValue(listener, "type", out var typeValue) &&
                IsTunScalar(typeValue))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetMappingValue(IDictionary mapping, string key, out object? value)
    {
        foreach (DictionaryEntry entry in mapping)
        {
            var entryKey = Convert.ToString(entry.Key, CultureInfo.InvariantCulture);
            if (!string.Equals(entryKey, key, StringComparison.OrdinalIgnoreCase))
                continue;

            value = entry.Value;
            return true;
        }

        value = null;
        return false;
    }

    private static bool IsTruthyScalar(object? value)
    {
        if (value is bool boolValue)
            return boolValue;

        if (value is byte byteValue)
            return byteValue == 1;
        if (value is sbyte sbyteValue)
            return sbyteValue == 1;
        if (value is short shortValue)
            return shortValue == 1;
        if (value is ushort ushortValue)
            return ushortValue == 1;
        if (value is int intValue)
            return intValue == 1;
        if (value is uint uintValue)
            return uintValue == 1;
        if (value is long longValue)
            return longValue == 1;
        if (value is ulong ulongValue)
            return ulongValue == 1;

        var normalized = Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim();
        return string.Equals(normalized, "true", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, "yes", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, "on", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, "1", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTunScalar(object? value)
    {
        var normalized = Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim();
        return string.Equals(normalized, "tun", StringComparison.OrdinalIgnoreCase);
    }
}

using System.Collections;
using System.Web.Script.Serialization;

namespace SingBoot;

/// <summary>
/// Holds core configuration details and any stdin payload required to start it.
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
        return LoadSingBox(profile.ConfigPath);
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
}

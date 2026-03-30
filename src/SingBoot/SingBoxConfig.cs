using System.Collections;
using System.Web.Script.Serialization;

namespace SingBoot;

/// <summary>
/// Holds the normalized JSON content of the sing-box configuration file.
/// </summary>
public sealed class SingBoxConfig
{
    /// <summary>Normalized JSON content ready to be piped to sing-box stdin.</summary>
    public string JsonContent { get; }
    public bool RequiresElevation { get; }

    private SingBoxConfig(string jsonContent, bool requiresElevation)
    {
        JsonContent = jsonContent;
        RequiresElevation = requiresElevation;
    }

    /// <summary>
    /// Reads and normalizes a sing-box config file (supports JSONC).
    /// </summary>
    public static SingBoxConfig Load(string configPath)
    {
        if (!File.Exists(configPath))
            throw new FileNotFoundException("Configuration file not found.", configPath);

        var rawJson = File.ReadAllText(configPath, System.Text.Encoding.UTF8);
        var normalized = JsonHelper.NormalizeJson(rawJson);

        if (string.IsNullOrWhiteSpace(normalized))
            throw new InvalidOperationException("Configuration file is empty or contains invalid JSON.");

        return new SingBoxConfig(normalized, DetectTunInbound(normalized));
    }

    private static bool DetectTunInbound(string normalizedJson)
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
}

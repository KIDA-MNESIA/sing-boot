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
        if (profile is null)
            throw new ArgumentNullException(nameof(profile));

        return LoadSingBox(profile.ConfigPath);
    }

    public static CoreConfig Parse(string rawJson)
    {
        if (rawJson is null)
            throw new ArgumentNullException(nameof(rawJson));

        var sanitized = StripUtf8Bom(rawJson);
        var normalized = JsonHelper.NormalizeJson(sanitized);

        if (string.IsNullOrWhiteSpace(normalized))
            throw new InvalidOperationException("Configuration file is empty or contains invalid JSON.");

        return new CoreConfig(normalized, DetectSingBoxTunInbound(normalized));
    }

    private static CoreConfig LoadSingBox(string configPath)
    {
        if (!File.Exists(configPath))
            throw new FileNotFoundException("Configuration file not found.", configPath);

        var rawJson = File.ReadAllText(configPath, System.Text.Encoding.UTF8);
        return Parse(rawJson);
    }

    internal static string StripUtf8Bom(string text)
    {
        return text.Length > 0 && text[0] == '\uFEFF'
            ? text.Substring(1)
            : text;
    }

    private static bool DetectSingBoxTunInbound(string normalizedJson)
    {
        object? rootObject;
        try
        {
            var serializer = new JavaScriptSerializer
            {
                MaxJsonLength = int.MaxValue
            };
            rootObject = serializer.DeserializeObject(normalizedJson);
        }
        catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
        {
            throw new InvalidOperationException("Configuration file is empty or contains invalid JSON.", ex);
        }

        if (rootObject is not IDictionary root)
            throw new InvalidOperationException("Configuration file is empty or contains invalid JSON.");

        if (!root.Contains("inbounds"))
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

using System.Collections;
using System.Text;
using System.Web.Script.Serialization;

namespace SingBoot;

/// <summary>
/// Optional application settings loaded from sing-boot.json next to sing-boot.exe.
/// </summary>
internal sealed class SingBootSettings
{
    public const string FileName = "sing-boot.json";
    private const string ConfigUrlPropertyName = "configUrl";

    public static SingBootSettings Empty { get; } = new(null);

    public Uri? ConfigUrl { get; }
    public bool HasConfigUrl => ConfigUrl is not null;

    private SingBootSettings(Uri? configUrl)
    {
        ConfigUrl = configUrl;
    }

    public static bool TryLoad(string baseDirectory, out SingBootSettings settings, out string message)
    {
        settings = Empty;

        string settingsPath;
        try
        {
            settingsPath = Path.Combine(Path.GetFullPath(baseDirectory), FileName);
        }
        catch (Exception ex)
        {
            message = $"Unable to resolve {FileName} path: {ex.Message}";
            return false;
        }

        if (!File.Exists(settingsPath))
        {
            message = "";
            return true;
        }

        string rawJson;
        try
        {
            rawJson = File.ReadAllText(settingsPath, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            message = $"Unable to read {FileName}: {ex.Message}";
            return false;
        }

        return TryParse(rawJson, out settings, out message);
    }

    public static bool TryParse(string rawJson, out SingBootSettings settings, out string message)
    {
        settings = Empty;

        if (rawJson is null)
        {
            message = $"{FileName} content is null.";
            return false;
        }

        object? rootObject;
        try
        {
            var sanitized = CoreConfig.StripUtf8Bom(rawJson);
            var normalized = JsonHelper.NormalizeJson(sanitized);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                message = $"{FileName} is empty or contains invalid JSON.";
                return false;
            }

            var serializer = new JavaScriptSerializer
            {
                MaxJsonLength = int.MaxValue
            };
            rootObject = serializer.DeserializeObject(normalized);
        }
        catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
        {
            message = $"{FileName} contains invalid JSON: {ex.Message}";
            return false;
        }

        if (rootObject is not IDictionary root)
        {
            message = $"{FileName} root must be a JSON object.";
            return false;
        }

        if (!TryGetPropertyIgnoreCase(root, ConfigUrlPropertyName, out var rawValue) || rawValue is null)
        {
            message = "";
            return true;
        }

        if (rawValue is not string urlString)
        {
            message = $"{FileName} property '{ConfigUrlPropertyName}' must be a string.";
            return false;
        }

        var trimmedUrl = urlString.Trim();
        if (trimmedUrl.Length == 0)
        {
            message = "";
            return true;
        }

        if (!Uri.TryCreate(trimmedUrl, UriKind.Absolute, out var uri) ||
            (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            message = $"{FileName} property '{ConfigUrlPropertyName}' must be an absolute HTTP or HTTPS URL.";
            return false;
        }

        settings = new SingBootSettings(uri);
        message = "";
        return true;
    }

    public static string DescribeUriForLog(Uri uri)
    {
        return uri.GetComponents(UriComponents.SchemeAndServer, UriFormat.Unescaped);
    }

    private static bool TryGetPropertyIgnoreCase(IDictionary dictionary, string propertyName, out object? value)
    {
        foreach (DictionaryEntry entry in dictionary)
        {
            if (entry.Key is string key &&
                string.Equals(key, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = entry.Value;
                return true;
            }
        }

        value = null;
        return false;
    }
}

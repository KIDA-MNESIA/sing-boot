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

    private static bool DetectMihomoTun(string rawYaml)
    {
        var lines = ReadYamlLines(rawYaml);

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line.Indent != 0 || !TryParseYamlPair(line.Text, out var key, out var value))
                continue;

            if (IsYamlKey(key, "tun") &&
                (InlineMappingHasScalar(value, "enable", IsTruthyScalar) ||
                 BlockHasScalar(lines, i + 1, line.Indent, "enable", IsTruthyScalar)))
            {
                return true;
            }

            if (IsYamlKey(key, "listeners") &&
                (InlineMappingHasScalar(value, "type", IsTunScalar) ||
                 BlockHasScalar(lines, i + 1, line.Indent, "type", IsTunScalar)))
            {
                return true;
            }
        }

        return false;
    }

    private static List<YamlLine> ReadYamlLines(string rawYaml)
    {
        var lines = new List<YamlLine>();
        using var reader = new StringReader(rawYaml);
        string? rawLine;

        while ((rawLine = reader.ReadLine()) is not null)
        {
            if (rawLine.Length > 0 && rawLine[0] == '\uFEFF')
                rawLine = rawLine.Substring(1);

            var withoutComment = StripYamlComment(rawLine);
            var indent = CountYamlIndent(withoutComment);
            var text = withoutComment.Substring(Math.Min(indent, withoutComment.Length)).TrimEnd();
            if (string.IsNullOrWhiteSpace(text) ||
                text == "---" ||
                text == "...")
            {
                continue;
            }

            lines.Add(new YamlLine(indent, text.TrimStart()));
        }

        return lines;
    }

    private static bool BlockHasScalar(
        IReadOnlyList<YamlLine> lines,
        int startIndex,
        int parentIndent,
        string targetKey,
        Func<string, bool> valuePredicate)
    {
        for (var i = startIndex; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line.Indent <= parentIndent && !line.Text.StartsWith("-", StringComparison.Ordinal))
                return false;

            if (TryParseYamlPair(line.Text, out var key, out var value) &&
                IsYamlKey(key, targetKey) &&
                valuePredicate(value))
                return true;
        }

        return false;
    }

    private static bool InlineMappingHasScalar(string value, string targetKey, Func<string, bool> valuePredicate)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
            return false;

        trimmed = trimmed.Trim('{', '}', '[', ']');
        foreach (var entry in SplitInlineYamlEntries(trimmed))
        {
            if (TryParseYamlPair(entry, out var key, out var entryValue) &&
                IsYamlKey(key, targetKey) &&
                valuePredicate(entryValue))
                return true;
        }

        return false;
    }

    private static IEnumerable<string> SplitInlineYamlEntries(string value)
    {
        var start = 0;
        var nested = 0;
        var inSingleQuote = false;
        var inDoubleQuote = false;

        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (ch == '\'' && !inDoubleQuote)
            {
                if (inSingleQuote && i + 1 < value.Length && value[i + 1] == '\'')
                {
                    i++;
                    continue;
                }

                inSingleQuote = !inSingleQuote;
                continue;
            }

            if (ch == '"' && !inSingleQuote && !IsEscaped(value, i))
            {
                inDoubleQuote = !inDoubleQuote;
                continue;
            }

            if (inSingleQuote || inDoubleQuote)
                continue;

            if (ch == '{' || ch == '[')
            {
                nested++;
                continue;
            }

            if ((ch == '}' || ch == ']') && nested > 0)
            {
                nested--;
                continue;
            }

            if (ch != ',' || nested != 0)
                continue;

            var entry = value.Substring(start, i - start).Trim();
            if (entry.Length > 0)
                yield return entry;

            start = i + 1;
        }

        var lastEntry = value.Substring(start).Trim();
        if (lastEntry.Length > 0)
            yield return lastEntry;
    }

    private static bool TryParseYamlPair(string text, out string key, out string value)
    {
        var normalized = text.Trim();
        if (normalized.StartsWith("-", StringComparison.Ordinal))
            normalized = normalized.Substring(1).TrimStart();

        var colonIndex = FindUnquotedColon(normalized);
        if (colonIndex <= 0)
        {
            key = "";
            value = "";
            return false;
        }

        key = NormalizeYamlScalar(normalized.Substring(0, colonIndex));
        value = normalized.Substring(colonIndex + 1).Trim();
        return key.Length > 0;
    }

    private static int FindUnquotedColon(string value)
    {
        var inSingleQuote = false;
        var inDoubleQuote = false;

        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (ch == '\'' && !inDoubleQuote)
            {
                if (inSingleQuote && i + 1 < value.Length && value[i + 1] == '\'')
                {
                    i++;
                    continue;
                }

                inSingleQuote = !inSingleQuote;
                continue;
            }

            if (ch == '"' && !inSingleQuote && !IsEscaped(value, i))
            {
                inDoubleQuote = !inDoubleQuote;
                continue;
            }

            if (ch == ':' && !inSingleQuote && !inDoubleQuote)
                return i;
        }

        return -1;
    }

    private static string StripYamlComment(string line)
    {
        var inSingleQuote = false;
        var inDoubleQuote = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '\'' && !inDoubleQuote)
            {
                if (inSingleQuote && i + 1 < line.Length && line[i + 1] == '\'')
                {
                    i++;
                    continue;
                }

                inSingleQuote = !inSingleQuote;
                continue;
            }

            if (ch == '"' && !inSingleQuote && !IsEscaped(line, i))
            {
                inDoubleQuote = !inDoubleQuote;
                continue;
            }

            if (ch == '#' && !inSingleQuote && !inDoubleQuote && (i == 0 || char.IsWhiteSpace(line[i - 1])))
                return line.Substring(0, i);
        }

        return line;
    }

    private static int CountYamlIndent(string line)
    {
        var count = 0;
        foreach (var ch in line)
        {
            if (ch == ' ')
            {
                count++;
                continue;
            }

            if (ch == '\t')
            {
                count += 2;
                continue;
            }

            break;
        }

        return count;
    }

    private static bool IsYamlKey(string value, string expected)
    {
        return string.Equals(NormalizeYamlScalar(value), expected, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTruthyScalar(string value)
    {
        var normalized = NormalizeYamlScalar(value);
        return normalized.Equals("true", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("on", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("1", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTunScalar(string value)
    {
        return string.Equals(NormalizeYamlScalar(value), "tun", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeYamlScalar(string value)
    {
        var normalized = value.Trim().TrimEnd(',');
        if (normalized.StartsWith("!!", StringComparison.Ordinal))
        {
            var tagEnd = normalized.IndexOfAny(new[] { ' ', '\t' });
            if (tagEnd >= 0)
                normalized = normalized.Substring(tagEnd + 1).TrimStart();
        }

        if (normalized.Length >= 2 &&
            ((normalized[0] == '\'' && normalized[normalized.Length - 1] == '\'') ||
             (normalized[0] == '"' && normalized[normalized.Length - 1] == '"')))
        {
            var quote = normalized[0];
            normalized = normalized.Substring(1, normalized.Length - 2);
            return quote == '\''
                ? normalized.Replace("''", "'")
                : normalized.Replace("\\\"", "\"").Replace("\\\\", "\\");
        }

        return normalized;
    }

    private static bool IsEscaped(string value, int index)
    {
        var slashCount = 0;
        for (var i = index - 1; i >= 0 && value[i] == '\\'; i--)
            slashCount++;

        return slashCount % 2 == 1;
    }

    private readonly struct YamlLine
    {
        public int Indent { get; }
        public string Text { get; }

        public YamlLine(int indent, string text)
        {
            Indent = indent;
            Text = text;
        }
    }
}

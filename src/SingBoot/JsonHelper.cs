using System.Text;

namespace SingBoot;

/// <summary>
/// Normalizes JSONC (JSON with comments and trailing commas) into strict JSON while
/// preserving token boundaries and source line numbers.
/// </summary>
public static class JsonHelper
{
    public static string NormalizeJson(string source)
    {
        if (source is null)
            throw new ArgumentNullException(nameof(source));

        var withoutComments = StripComments(source);
        return RemoveTrailingCommas(withoutComments);
    }

    private static string StripComments(string source)
    {
        var result = new StringBuilder(source.Length);
        var inString = false;
        var escape = false;
        var inSingleLineComment = false;
        var inMultiLineComment = false;

        for (var i = 0; i < source.Length; i++)
        {
            var ch = source[i];

            if (inSingleLineComment)
            {
                if (ch == '\r' || ch == '\n')
                {
                    inSingleLineComment = false;
                    result.Append(ch);
                }
                else
                {
                    result.Append(' ');
                }

                continue;
            }

            if (inMultiLineComment)
            {
                if (ch == '*' && i + 1 < source.Length && source[i + 1] == '/')
                {
                    result.Append("  ");
                    i++;
                    inMultiLineComment = false;
                }
                else
                {
                    result.Append(ch == '\r' || ch == '\n' ? ch : ' ');
                }

                continue;
            }

            if (inString)
            {
                result.Append(ch);
                if (escape)
                {
                    escape = false;
                }
                else if (ch == '\\')
                {
                    escape = true;
                }
                else if (ch == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (ch == '"')
            {
                inString = true;
                result.Append(ch);
                continue;
            }

            if (ch == '/' && i + 1 < source.Length)
            {
                var next = source[i + 1];
                if (next == '/')
                {
                    result.Append("  ");
                    i++;
                    inSingleLineComment = true;
                    continue;
                }

                if (next == '*')
                {
                    result.Append("  ");
                    i++;
                    inMultiLineComment = true;
                    continue;
                }
            }

            result.Append(ch);
        }

        if (inMultiLineComment)
            throw new InvalidOperationException("Configuration contains an unterminated block comment.");
        if (inString)
            throw new InvalidOperationException("Configuration contains an unterminated JSON string.");

        return result.ToString();
    }

    private static string RemoveTrailingCommas(string source)
    {
        var result = new StringBuilder(source.Length);
        var inString = false;
        var escape = false;

        for (var i = 0; i < source.Length; i++)
        {
            var ch = source[i];

            if (inString)
            {
                result.Append(ch);
                if (escape)
                {
                    escape = false;
                }
                else if (ch == '\\')
                {
                    escape = true;
                }
                else if (ch == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (ch == '"')
            {
                inString = true;
                result.Append(ch);
                continue;
            }

            if (ch == ',')
            {
                var nextIndex = i + 1;
                while (nextIndex < source.Length && IsJsonWhitespace(source[nextIndex]))
                    nextIndex++;

                if (nextIndex < source.Length && (source[nextIndex] == '}' || source[nextIndex] == ']'))
                    continue;
            }

            result.Append(ch);
        }

        return result.ToString();
    }

    private static bool IsJsonWhitespace(char ch)
    {
        return ch == ' ' || ch == '\t' || ch == '\r' || ch == '\n';
    }
}

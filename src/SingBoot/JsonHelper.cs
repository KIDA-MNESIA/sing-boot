using System.Text;

namespace SingBoot;

/// <summary>
/// Normalizes JSONC (JSON with comments and trailing commas) into strict JSON.
/// Direct port of the Delphi <c>NormalizeJson</c> state-machine parser.
/// </summary>
public static class JsonHelper
{
    public static string NormalizeJson(string source)
    {
        var len = source.Length;
        var sb = new StringBuilder(len);

        var i = 0;
        var inString = false;
        var escape = false;
        var inSingleLineComment = false;
        var inMultiLineComment = false;
        var pendingComma = false;

        while (i < len)
        {
            var ch = source[i];

            if (inSingleLineComment)
            {
                if (ch == '\n' || ch == '\r')
                    inSingleLineComment = false;
                i++;
                continue;
            }

            if (inMultiLineComment)
            {
                if (ch == '*' && i + 1 < len && source[i + 1] == '/')
                {
                    inMultiLineComment = false;
                    i += 2;
                }
                else
                {
                    i++;
                }
                continue;
            }

            if (inString)
            {
                sb.Append(ch);
                if (escape)
                {
                    escape = false;
                }
                else
                {
                    if (ch == '\\')
                        escape = true;
                    else if (ch == '"')
                        inString = false;
                }
                i++;
                continue;
            }

            // Check for comments
            if (ch == '/' && i + 1 < len)
            {
                var nextCh = source[i + 1];
                if (nextCh == '/')
                {
                    inSingleLineComment = true;
                    i += 2;
                    continue;
                }
                if (nextCh == '*')
                {
                    inMultiLineComment = true;
                    i += 2;
                    continue;
                }
            }

            // Trailing comma handling
            if (ch == ',')
            {
                pendingComma = true;
                i++;
                continue;
            }

            // Skip whitespace outside strings
            if (ch == ' ' || ch == '\t' || ch == '\n' || ch == '\r')
            {
                i++;
                continue;
            }

            // Flush pending comma (skip if followed by closing bracket)
            if (pendingComma)
            {
                if (ch != '}' && ch != ']')
                    sb.Append(',');
                pendingComma = false;
            }

            if (ch == '"')
            {
                inString = true;
                escape = false;
                sb.Append(ch);
                i++;
                continue;
            }

            sb.Append(ch);
            i++;
        }

        return sb.ToString();
    }
}

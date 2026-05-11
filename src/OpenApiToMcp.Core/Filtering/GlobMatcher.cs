using System.Text;
using System.Text.RegularExpressions;

namespace OpenApiToMcp.Core.Filtering;

/// <summary>
/// Simple glob pattern matcher supporting *, ?, and ** wildcards.
/// </summary>
public static class GlobMatcher
{
    /// <summary>
    /// Tests whether <paramref name="text"/> matches the glob <paramref name="pattern"/>.
    /// Supports: * (any chars except /), ? (single char), ** (any chars including /).
    /// </summary>
    public static bool Matches(string text, string pattern)
    {
        var regex = GlobToRegex(pattern);
        return Regex.IsMatch(text, regex, RegexOptions.IgnoreCase | RegexOptions.Singleline);
    }

    private static string GlobToRegex(string glob)
    {
        var sb = new StringBuilder("^");
        int i = 0;
        while (i < glob.Length)
        {
            if (i + 1 < glob.Length && glob[i] == '*' && glob[i + 1] == '*')
            {
                sb.Append(".*");
                i += 2;
            }
            else if (glob[i] == '*')
            {
                sb.Append("[^/]*");
                i++;
            }
            else if (glob[i] == '?')
            {
                sb.Append("[^/]");
                i++;
            }
            else if (".+^${}()|[]\\".Contains(glob[i]))
            {
                sb.Append('\\').Append(glob[i]);
                i++;
            }
            else
            {
                sb.Append(glob[i]);
                i++;
            }
        }
        sb.Append('$');
        return sb.ToString();
    }
}

using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;

namespace Lightkeeper.Functions.Routing;

/// <summary>
/// Wildcard patterns used by notification rules. <c>*</c> matches within one folder level, <c>**</c> matches any number of levels,
/// <c>?</c> matches one character, and everything else (including <c>[ ]</c>) is literal. Matching ignores case.
/// </summary>
public static class RulePattern
{
    public const char FolderSeparator = '\\';
    public const string RootFolder = "\\";

    private const char AlternateFolderSeparator = '/';
    private const string AnyDepth = "**";
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);
    private static readonly ConcurrentDictionary<string, Regex> Cache = new(StringComparer.Ordinal);

    /// <summary>An empty pattern matches every value.</summary>
    public static bool IsMatch(string? pattern, string value) =>
        string.IsNullOrWhiteSpace(pattern) || Cache.GetOrAdd(pattern.Trim(), Build).IsMatch(value);

    /// <summary>Folder in the form Azure DevOps uses: a leading <c>\</c>, no trailing <c>\</c>, and <c>\</c> for the root. Accepts <c>/</c>.</summary>
    public static string NormalizeFolder(string? folder)
    {
        var normalized = (folder ?? string.Empty).Trim().Replace(AlternateFolderSeparator, FolderSeparator).TrimEnd(FolderSeparator);
        return normalized.StartsWith(FolderSeparator) ? normalized : FolderSeparator + normalized;
    }

    private static Regex Build(string pattern)
    {
        var anyDepthFolder = FolderSeparator + AnyDepth;
        var regex = new StringBuilder("^");
        for (var i = 0; i < pattern.Length;)
        {
            if (string.CompareOrdinal(pattern, i, anyDepthFolder, 0, anyDepthFolder.Length) == 0)
            {
                regex.Append(@"(?:\\.*)?");
                i += anyDepthFolder.Length;
            }
            else if (string.CompareOrdinal(pattern, i, AnyDepth, 0, AnyDepth.Length) == 0)
            {
                regex.Append(".*");
                i += AnyDepth.Length;
            }
            else
            {
                regex.Append(pattern[i] switch
                {
                    '*' => @"[^\\]*",
                    '?' => @"[^\\]",
                    var literal => Regex.Escape(literal.ToString()),
                });
                i++;
            }
        }

        return new Regex(regex.Append('$').ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, MatchTimeout);
    }
}

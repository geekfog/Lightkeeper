namespace Lightkeeper.Functions.Routing;

/// <summary>
/// A rule's status list, such as <c>Partially Succeeded, Failed, Stopped</c>. A leading <c>!</c> negates the whole list, so <c>!Succeeded</c>
/// matches every other status. Each entry may be the name Azure DevOps shows or the value the event carries; spaces and case are ignored.
/// </summary>
public static class StatusFilter
{
    public const char Negation = '!';
    private static readonly char[] Separators = [',', ';'];

    /// <summary>An empty filter matches every status.</summary>
    public static bool IsMatch(string? filter, EventFacts facts)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        var list = filter.Trim();
        var negate = list.StartsWith(Negation);
        var listed = list.TrimStart(Negation)
            .Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Normalize)
            .Any(entry => entry == Normalize(facts.Status) || entry == Normalize(facts.StatusValue));

        return negate ? !listed : listed;
    }

    private static string Normalize(string value) => value.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
}

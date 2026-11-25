using System.Text.Json;
using Lightkeeper.Functions.Models;
using Lightkeeper.Functions.Routing;

namespace Lightkeeper.Functions.Cards;

/// <summary>
/// Card for every event without its own formatter, built from the summary Azure DevOps sends with all events (<c>message</c> and <c>detailedMessage</c>).
/// For rules only <c>Trigger</c> and <c>Project</c> mean something; folder, pipeline, and status are empty.
/// </summary>
public sealed class AnyEventCardFormatter : IServiceHookCardFormatter
{
    /// <summary>The <see cref="EventType"/> the notifier falls back to when no formatter is registered for an event.</summary>
    public const string AnyEventType = "*";

    public const string ProjectFactName = "📁 Project";
    public const string OpenLinkName = "Open in Azure DevOps";

    private const int MaxDetailsChars = 1_500;
    private const string WorkItemEventPrefix = "workitem.";

    /// <summary>Where events keep their project name, in the order tried; work items keep it in <c>fields</c>.</summary>
    private static readonly string[][] ProjectPaths =
    [
        ["project", "name"],
        ["repository", "project", "name"],
        ["definition", "project", "name"],
        ["pipeline", "project", "name"],
        ["release", "project", "name"],
        ["fields", "System.TeamProject"],
        ["revision", "fields", "System.TeamProject"],
    ];

    /// <summary>Where events keep their web page, in the order tried; API addresses (<c>url</c>) are not offered.</summary>
    private static readonly string[][] LinkPaths = [["_links", "web", "href"], ["_links", "html", "href"]];

    public string EventType => AnyEventType;

    public CardFormatResult Format(ServiceHookEvent serviceHookEvent)
    {
        var resource = serviceHookEvent.Resource;
        var eventType = serviceHookEvent.EventType!;
        var title = FirstText(serviceHookEvent.Message?.Text, eventType)!;
        var details = CardStyle.MarkdownExcerpt(FirstText(serviceHookEvent.DetailedMessage?.Markdown, serviceHookEvent.Message?.Markdown), MaxDetailsChars);
        var project = ProjectPaths.Select(path => Find(resource, path)).FirstOrDefault(p => p is not null);

        var card = CardStyle.Card(
            eventType.StartsWith(WorkItemEventPrefix, StringComparison.OrdinalIgnoreCase) ? CardStyle.WorkItem : CardStyle.Info,
            title,
            eventType,
            [(ProjectFactName, project)],
            [(OpenLinkName, CardStyle.HttpsLink([.. LinkPaths.Select(path => Find(resource, path))]))],
            details == title ? null : details);

        var facts = new EventFacts(project, RulePattern.RootFolder, string.Empty, string.Empty, string.Empty, serviceHookEvent.Id, CardStyle.Excerpt(title, MaxDetailsChars));
        return CardFormatResult.Success(card, facts);
    }

    private static string? FirstText(params string?[] candidates) => candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));

    private static string? Find(JsonElement element, IEnumerable<string> path)
    {
        foreach (var name in path)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out element))
            {
                return null;
            }
        }

        return element.ValueKind == JsonValueKind.String ? element.GetString() : null;
    }
}

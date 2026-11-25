using Lightkeeper.Functions.Models;

namespace Lightkeeper.Functions.Routing;

public enum RuleAction
{
    Include,
    Exclude,
}

/// <summary>What an Include rule posts. Empty, missing, or unrecognized values in the table mean <see cref="Card"/>.</summary>
public enum NotificationType
{
    /// <summary>The event's own card.</summary>
    Card,

    /// <summary>For <c>git.pullrequest.*</c> events: a table of the project's active pull requests instead of the pull request card. Other events get <see cref="Card"/>.</summary>
    ActiveList,
}

/// <summary>One row of the <c>NotificationRule</c> table. Empty match columns match anything.</summary>
/// <param name="Workflows">Teams Workflow IDs an Include rule posts to; empty means <see cref="WorkflowIds.Fallback"/>.</param>
/// <param name="NotificationType">What an Include rule posts.</param>
public sealed record NotificationRule(
    string Organization,
    string RuleId,
    string? Project,
    int Priority,
    string? Trigger,
    string? Folder,
    string? Pipeline,
    string? Status,
    RuleAction Action,
    IReadOnlyList<string> Workflows,
    string? Note,
    NotificationType NotificationType = NotificationType.Card)
{
    public bool AppliesTo(NotificationSubject subject) =>
        RulePattern.IsMatch(Organization, subject.Organization)
        && RulePattern.IsMatch(Project, subject.Facts.Project ?? string.Empty)
        && RulePattern.IsMatch(Trigger, subject.Trigger)
        && RulePattern.IsMatch(string.IsNullOrWhiteSpace(Folder) ? null : RulePattern.NormalizeFolder(Folder), subject.Facts.Folder)
        && RulePattern.IsMatch(Pipeline, subject.Facts.Pipeline)
        && StatusFilter.IsMatch(Status, subject.Facts);

    /// <summary>Whether this rule posts the active pull request list for <paramref name="subject"/>, which only a pull request event can.</summary>
    public bool PostsActiveList(NotificationSubject subject) =>
        NotificationType == NotificationType.ActiveList && ServiceHookEventTypes.IsPullRequest(subject.Trigger);

    /// <summary>The applicable rule with the lowest priority number; ties go to the alphabetically first rule ID.</summary>
    public static NotificationRule? FirstMatch(IEnumerable<NotificationRule> rules, NotificationSubject subject) =>
        rules.OrderBy(r => r.Priority).ThenBy(r => r.RuleId, StringComparer.Ordinal).FirstOrDefault(r => r.AppliesTo(subject));
}

public static class WorkflowIds
{
    /// <summary>The Teams Workflow in the <c>TeamsWorkflow__FallbackWebhookUrl</c> app setting.</summary>
    public const string Fallback = "fallback";

    public const char Separator = ';';

    public static IReadOnlyList<string> Parse(string? list) =>
        (list ?? string.Empty).Split(Separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static string Join(IEnumerable<string> ids) => string.Join($"{Separator} ", ids);
}

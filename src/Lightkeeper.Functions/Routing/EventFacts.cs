namespace Lightkeeper.Functions.Routing;

/// <summary>What notification rules match against, extracted from an event by its card formatter.</summary>
/// <param name="Folder">Normalized with <see cref="RulePattern.NormalizeFolder"/>.</param>
/// <param name="Status">The name Azure DevOps shows, such as <c>Stopped</c>.</param>
/// <param name="StatusValue">The value the event carries, such as <c>canceled</c>.</param>
/// <param name="Reference">Identifies the run, such as a build number.</param>
/// <param name="Path">What the log shows in its <c>Pipeline</c> column; defaults to the folder and pipeline.</param>
public sealed record EventFacts(string? Project, string Folder, string Pipeline, string Status, string StatusValue, string? Reference, string? Path = null);

/// <summary>An event's facts with the organization and trigger they arrived under.</summary>
public sealed record NotificationSubject(string Organization, string Trigger, EventFacts Facts)
{
    public string PipelinePath => Facts.Path ??
        (Facts.Folder == RulePattern.RootFolder ? RulePattern.RootFolder + Facts.Pipeline : Facts.Folder + RulePattern.FolderSeparator + Facts.Pipeline);
}

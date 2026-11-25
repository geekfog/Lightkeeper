using Lightkeeper.Functions.Integrations.TableStorage;
using Lightkeeper.Functions.Routing;

namespace Lightkeeper.Functions.Tests.TestSupport;

/// <summary>In-memory rules, workflow URLs, and announcements; each can be made unreadable.</summary>
internal sealed class FakeNotificationRuleStore : INotificationRuleStore, INotificationAnnouncementStore
{
    private readonly Lock _announcedLock = new();

    public HashSet<(string Organization, string Key)> Announced { get; } = [];
    public bool AnnouncementsUnreadable { get; set; }

    public List<NotificationRule> Rules { get; } = [];
    public Dictionary<string, string> WorkflowUrls { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool RulesUnreadable { get; set; }
    public bool WorkflowsUnreadable { get; set; }

    public Task<IReadOnlyList<NotificationRule>> GetRulesAsync(CancellationToken cancellationToken) =>
        RulesUnreadable ? throw new InvalidOperationException("rules offline") : Task.FromResult<IReadOnlyList<NotificationRule>>(Rules);

    public Task<IReadOnlyDictionary<string, string>> GetWorkflowUrlsAsync(CancellationToken cancellationToken) =>
        WorkflowsUnreadable ? throw new InvalidOperationException("workflows offline") : Task.FromResult<IReadOnlyDictionary<string, string>>(WorkflowUrls);

    /// <summary>When each announced key was claimed.</summary>
    public Dictionary<(string Organization, string Key), DateTimeOffset> AnnouncedTimes { get; } = [];

    public Task<bool> TryClaimAsync(
        string organization, string key, NotificationSubject subject, string correlationId, DateTimeOffset announcedUtc, CancellationToken cancellationToken)
    {
        if (AnnouncementsUnreadable) throw new InvalidOperationException("announcements offline");
        lock (_announcedLock)
        {
            if (!Announced.Add((organization, key))) return Task.FromResult(false);
            AnnouncedTimes[(organization, key)] = announcedUtc;
            return Task.FromResult(true);
        }
    }

    public Task<IReadOnlyDictionary<string, DateTimeOffset>> GetAnnouncedTimesAsync(
        string organization, IReadOnlyCollection<string> keys, CancellationToken cancellationToken)
    {
        if (AnnouncementsUnreadable) throw new InvalidOperationException("announcements offline");
        lock (_announcedLock)
        {
            IReadOnlyDictionary<string, DateTimeOffset> times = keys.Where(k => AnnouncedTimes.ContainsKey((organization, k)))
                .ToDictionary(k => k, k => AnnouncedTimes[(organization, k)], StringComparer.Ordinal);
            return Task.FromResult(times);
        }
    }

    public Task ReleaseAsync(string organization, string key, CancellationToken cancellationToken)
    {
        lock (_announcedLock)
        {
            Announced.Remove((organization, key));
            AnnouncedTimes.Remove((organization, key));
        }

        return Task.CompletedTask;
    }

    /// <summary>A rule with every match column empty unless set.</summary>
    public static NotificationRule Rule(
        string ruleId,
        int priority,
        RuleAction action,
        string? workflows = null,
        string organization = "*",
        string? project = null,
        string? trigger = null,
        string? folder = null,
        string? pipeline = null,
        string? status = null,
        NotificationType notificationType = NotificationType.Card) =>
        new(organization, ruleId, project, priority, trigger, folder, pipeline, status, action, WorkflowIds.Parse(workflows), null, notificationType);
}

using Lightkeeper.Functions.Notifications;
using Lightkeeper.Functions.Routing;

namespace Lightkeeper.Functions.Integrations.TableStorage;

/// <summary>Reads notification rules and Teams Workflow URLs. Both are read on every event so edits apply without a release or restart.</summary>
public interface INotificationRuleStore
{
    /// <summary>Active, valid rules. Inactive rows are ignored; active rows that cannot be read as a rule are skipped and logged.</summary>
    Task<IReadOnlyList<NotificationRule>> GetRulesAsync(CancellationToken cancellationToken);

    /// <summary>Workflow ID to webhook URL. URLs are secrets.</summary>
    Task<IReadOnlyDictionary<string, string>> GetWorkflowUrlsAsync(CancellationToken cancellationToken);
}

/// <summary>Remembers subjects announced once, such as pull requests, so later events about them do not post again.</summary>
public interface INotificationAnnouncementStore
{
    /// <summary>Atomically records the subject as announced at <paramref name="announcedUtc"/>; false when it already was, so only one of several simultaneous events can post.</summary>
    Task<bool> TryClaimAsync(
        string organization, string key, NotificationSubject subject, string correlationId, DateTimeOffset announcedUtc, CancellationToken cancellationToken);

    /// <summary>When each of <paramref name="keys"/> was announced; keys never announced are left out.</summary>
    Task<IReadOnlyDictionary<string, DateTimeOffset>> GetAnnouncedTimesAsync(string organization, IReadOnlyCollection<string> keys, CancellationToken cancellationToken);

    /// <summary>Removes a claim whose card was not delivered anywhere, so the next event tries again.</summary>
    Task ReleaseAsync(string organization, string key, CancellationToken cancellationToken);
}

public interface INotificationLogStore
{
    Task WriteAsync(NotificationLogEntry entry, CancellationToken cancellationToken);

    /// <summary>Deletes every row received before <paramref name="cutoff"/> (UTC) and returns how many were deleted.</summary>
    Task<int> DeleteBeforeAsync(DateOnly cutoff, CancellationToken cancellationToken);
}

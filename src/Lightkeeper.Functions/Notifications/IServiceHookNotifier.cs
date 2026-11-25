using Lightkeeper.Functions.Routing;

namespace Lightkeeper.Functions.Notifications;

/// <summary>Validates a service-hook payload, applies notification rules, formats its card, and delivers it. Independent of the Functions host.</summary>
public interface IServiceHookNotifier
{
    Task<NotificationResult> NotifyAsync(string payload, string correlationId, CancellationToken cancellationToken);
}

public enum NotificationOutcome
{
    WorkflowAccepted,
    Skipped,
    InvalidPayload,
    WorkflowRejected,
    WorkflowTimedOut,
    WorkflowUnreachable,
    WorkflowNotConfigured,
}

/// <summary>How the rules decided. <see cref="None"/> means the event never reached the rules because it was invalid.</summary>
public enum RoutingDecision
{
    None,
    Notify,
    Skip,
    SkipNoRule,
    NotifyRulesUnreadable,
    NotifyWorkflowsUnreadable,

    /// <summary>The event itself is not worth a card, such as a draft pull request; see <see cref="NotificationTrace.SkipReason"/>.</summary>
    SkipByEvent,

    /// <summary>The rule chose to notify, but this subject was already announced once.</summary>
    SkipAlreadyAnnounced,

    /// <summary>The rule chose the active pull request list, but Azure DevOps could not supply it, so the event's own card was posted.</summary>
    NotifyActiveListUnavailable,
}

/// <param name="StatusCode">HTTP status the workflow returned, when it answered.</param>
public sealed record WorkflowDelivery(string WorkflowId, NotificationOutcome Outcome, int? StatusCode);

/// <summary>What Lightkeeper knew and decided about one request; the source of its <c>NotificationLog</c> row.</summary>
/// <param name="SkipReason">Why the event was not worth a card, for <see cref="RoutingDecision.SkipByEvent"/>.</param>
public sealed record NotificationTrace(
    NotificationSubject? Subject, NotificationRule? Rule, RoutingDecision Decision, IReadOnlyList<WorkflowDelivery> Deliveries, string? SkipReason = null)
{
    public static readonly NotificationTrace Empty = new(null, null, RoutingDecision.None, []);
}

/// <param name="Outcome">The first failed delivery's outcome, or the overall outcome when every delivery succeeded or none was attempted.</param>
public sealed record NotificationResult(NotificationOutcome Outcome, string Detail, NotificationTrace Trace);

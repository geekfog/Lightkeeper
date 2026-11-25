using Lightkeeper.Functions.Routing;

namespace Lightkeeper.Functions.Notifications;

/// <summary>One <c>NotificationLog</c> row: what arrived, what the rules decided, and what each Teams Workflow answered. Property names are the column names.</summary>
/// <param name="Workflow">Workflow IDs in delivery order, separated by <see cref="WorkflowIds.Separator"/>.</param>
/// <param name="Outcome">One outcome per workflow, in the same order as <paramref name="Workflow"/>, or the overall outcome when nothing was delivered.</param>
/// <param name="Payload">The request body exactly as received.</param>
public sealed record NotificationLogEntry(
    DateTimeOffset ReceivedUtc,
    string CorrelationId,
    string? Trigger,
    string? Organization,
    string? Project,
    string? Pipeline,
    string? Reference,
    string? Status,
    string? RuleId,
    int? RulePriority,
    string? RuleAction,
    string Decision,
    string? Workflow,
    string Outcome,
    int HttpStatus,
    string Detail,
    long DurationMs,
    string Payload)
{
    /// <summary>The decision as logged, naming the rule that made it, such as <c>Notify (build-failures)</c> or <c>Skip (no rule)</c>.</summary>
    internal static string DescribeDecision(RoutingDecision decision, NotificationRule? rule, string? skipReason = null) => decision switch
    {
        RoutingDecision.SkipByEvent => $"Skip ({skipReason})",
        RoutingDecision.SkipAlreadyAnnounced => $"Skip ({rule?.RuleId}, already announced)",
        RoutingDecision.Notify => $"Notify ({rule?.RuleId})",
        RoutingDecision.Skip => $"Skip ({rule?.RuleId})",
        RoutingDecision.SkipNoRule => "Skip (no rule)",
        RoutingDecision.NotifyRulesUnreadable => "Notify (rules unreadable)",
        RoutingDecision.NotifyWorkflowsUnreadable => $"Notify ({rule?.RuleId}, workflows unreadable)",
        RoutingDecision.NotifyActiveListUnavailable => $"Notify ({rule?.RuleId}, active list unavailable)",
        _ => string.Empty,
    };

    public static NotificationLogEntry Create(
        NotificationResult result, string correlationId, string payload, DateTimeOffset receivedUtc, int httpStatus, TimeSpan duration)
    {
        var trace = result.Trace;
        var subject = trace.Subject;
        var deliveries = trace.Deliveries;

        return new NotificationLogEntry(
            receivedUtc,
            correlationId,
            subject?.Trigger,
            subject?.Organization,
            subject?.Facts.Project,
            subject?.PipelinePath,
            subject?.Facts.Reference,
            subject?.Facts.Status,
            trace.Rule?.RuleId,
            trace.Rule?.Priority,
            trace.Rule?.Action.ToString(),
            DescribeDecision(trace.Decision, trace.Rule, trace.SkipReason),
            deliveries.Count == 0 ? null : WorkflowIds.Join(deliveries.Select(d => d.WorkflowId)),
            deliveries.Count == 0 ? result.Outcome.ToString() : WorkflowIds.Join(deliveries.Select(d => d.Outcome.ToString())),
            httpStatus,
            result.Detail,
            (long)duration.TotalMilliseconds,
            payload);
    }
}

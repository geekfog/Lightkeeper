using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Lightkeeper.Client.AzDo;
using Lightkeeper.Functions.Cards;
using Lightkeeper.Functions.Integrations.TableStorage;
using Lightkeeper.Functions.Integrations.TeamsWorkflow;
using Lightkeeper.Functions.Models;
using Lightkeeper.Functions.Routing;

namespace Lightkeeper.Functions.Notifications;

public sealed partial class ServiceHookNotifier(
    IEnumerable<IServiceHookCardFormatter> formatters,
    INotificationRuleStore ruleStore,
    INotificationAnnouncementStore announcementStore,
    ITeamsWorkflowClient teamsWorkflow,
    IOptions<TeamsWorkflowOptions> teamsWorkflowOptions,
    IAzDoClient azDo,
    TimeProvider timeProvider,
    ILogger<ServiceHookNotifier> logger) : IServiceHookNotifier
{
    private static readonly IReadOnlyList<string> FallbackWorkflowOnly = [WorkflowIds.Fallback];

    private readonly Dictionary<string, IServiceHookCardFormatter> _formatters =
        formatters.ToDictionary(f => f.EventType, StringComparer.OrdinalIgnoreCase);

    public async Task<NotificationResult> NotifyAsync(string payload, string correlationId, CancellationToken cancellationToken)
    {
        ServiceHookEvent? serviceHookEvent;
        try
        {
            serviceHookEvent = string.IsNullOrWhiteSpace(payload) ? null : JsonSerializer.Deserialize<ServiceHookEvent>(payload, LightkeeperJson.Options);
        }
        catch (JsonException ex)
        {
            return Invalid($"Request body is not a valid service-hook event: {ex.Message}");
        }

        if (serviceHookEvent is null)
        {
            return Invalid("Request body is empty.");
        }

        if (string.IsNullOrWhiteSpace(serviceHookEvent.EventType))
        {
            return Invalid("eventType is required.");
        }

        LogReceived(logger, serviceHookEvent.EventType, serviceHookEvent.Id, serviceHookEvent.SubscriptionId, serviceHookEvent.NotificationId);

        // Events without their own formatter get the general card, so every Azure DevOps event can be routed.
        var formatter = _formatters.GetValueOrDefault(serviceHookEvent.EventType) ?? _formatters[AnyEventCardFormatter.AnyEventType];

        CardFormatResult formatted;
        try
        {
            formatted = formatter.Format(serviceHookEvent);
        }
        catch (JsonException ex)
        {
            return Invalid($"resource does not match the {serviceHookEvent.EventType} schema: {ex.Message}");
        }

        if (formatted.Facts is not { } facts || (formatted.Card is null && formatted.SkipReason is null))
        {
            return Invalid(string.Join(" ", formatted.Errors));
        }

        var subject = new NotificationSubject(AzureDevOpsOrganization.From(serviceHookEvent) ?? string.Empty, serviceHookEvent.EventType, facts);
        if (formatted.SkipReason is { } skipReason)
        {
            return new NotificationResult(
                NotificationOutcome.Skipped, $"Skipped: {skipReason}.", new NotificationTrace(subject, null, RoutingDecision.SkipByEvent, [], skipReason));
        }

        var card = formatted.Card!;
        var (rule, decision) = await RouteAsync(subject, cancellationToken);

        if (decision is RoutingDecision.Skip or RoutingDecision.SkipNoRule)
        {
            var detail = rule is null ? "Skipped: no active rule matched." : $"Skipped by rule '{rule.RuleId}' (priority {rule.Priority}).";
            return new NotificationResult(NotificationOutcome.Skipped, detail, new NotificationTrace(subject, rule, decision, []));
        }

        var onceKey = formatted.AnnounceOnceKey;
        var claim = onceKey is null ? AnnouncementClaim.NotNeeded : await ClaimAnnouncementAsync(subject, onceKey, correlationId, cancellationToken);
        if (claim == AnnouncementClaim.AlreadyAnnounced)
        {
            return new NotificationResult(
                NotificationOutcome.Skipped,
                $"Skipped: {facts.Reference} was already announced.",
                new NotificationTrace(subject, rule, RoutingDecision.SkipAlreadyAnnounced, []));
        }

        var listNote = string.Empty;
        if (rule?.PostsActiveList(subject) == true)
        {
            (card, decision, listNote) = await ActiveListCardAsync(subject, card, cancellationToken);
        }

        var workflowIds = rule is { Workflows.Count: > 0 } ? rule.Workflows : FallbackWorkflowOnly;
        var (targets, resolvedDecision) = await ResolveWorkflowsAsync(workflowIds, decision, cancellationToken);
        var deliveries = await Task.WhenAll(targets.Select(t => DeliverAsync(t.Id, t.Url, card, correlationId, cancellationToken)));

        if (claim == AnnouncementClaim.Claimed && !deliveries.Any(d => d.Outcome == NotificationOutcome.WorkflowAccepted))
        {
            await ReleaseAnnouncementAsync(subject, onceKey!, cancellationToken);
        }

        var failed = deliveries.FirstOrDefault(d => d.Outcome != NotificationOutcome.WorkflowAccepted);
        var result = new NotificationResult(
            failed?.Outcome ?? NotificationOutcome.WorkflowAccepted,
            Describe(deliveries) + listNote,
            new NotificationTrace(subject, rule, resolvedDecision, deliveries));

        if (failed is not null)
        {
            LogNotDelivered(logger, result.Outcome, result.Detail);
        }

        return result;
    }

    /// <summary>First matching active rule by priority; no match skips. When the rules cannot be read, notifies the fallback workflow rather than miss a notification.</summary>
    private async Task<(NotificationRule? Rule, RoutingDecision Decision)> RouteAsync(NotificationSubject subject, CancellationToken cancellationToken)
    {
        IReadOnlyList<NotificationRule> rules;
        try
        {
            rules = await ruleStore.GetRulesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogStoreUnreadable(logger, NotificationTables.Rule, ex.Message);
            return (null, RoutingDecision.NotifyRulesUnreadable);
        }

        var rule = NotificationRule.FirstMatch(rules, subject);
        return rule switch
        {
            null => (null, RoutingDecision.SkipNoRule),
            { Action: RuleAction.Exclude } => (rule, RoutingDecision.Skip),
            _ => (rule, RoutingDecision.Notify),
        };
    }

    /// <summary>The table of active pull requests in the event's project, with each shown row's comment count looked up in parallel. When Azure DevOps cannot supply it, the event's own card posts instead of nothing.</summary>
    private async Task<(AdaptiveCard Card, RoutingDecision Decision, string Note)> ActiveListCardAsync(
        NotificationSubject subject, AdaptiveCard eventCard, CancellationToken cancellationToken)
    {
        var listing = string.IsNullOrWhiteSpace(subject.Organization) || string.IsNullOrWhiteSpace(subject.Facts.Project)
            ? null
            : await azDo.GetActivePullRequestsAsync(subject.Organization, subject.Facts.Project, cancellationToken);

        if (listing is { Succeeded: true, Value: { } pullRequests })
        {
            var announcedAt = await AnnouncedTimesAsync(subject.Organization, pullRequests, cancellationToken);
            var reviewable = ActivePullRequestListCard.Reviewable(pullRequests, announcedAt);
            var counts = await Task.WhenAll(ActivePullRequestListCard.Shown(reviewable, subject.Facts.Reference)
                .Select(async pr => (pr.Id, Result: await azDo.GetCommentCountAsync(subject.Organization, subject.Facts.Project!, pr, cancellationToken))));
            var comments = counts.Where(c => c.Result is { Succeeded: true, Value: not null }).ToDictionary(c => c.Id, c => c.Result.Value!);
            var missing = counts.Length - comments.Count;
            return (ActivePullRequestListCard.Create(subject, reviewable, comments, announcedAt, timeProvider.GetUtcNow()), RoutingDecision.Notify,
                missing == 0 ? string.Empty : $" Comment counts unavailable for {missing} pull request(s).");
        }

        var reason = listing is null ? "the event has no organization or project" : $"Azure DevOps answered {listing.Status}";
        return (eventCard, RoutingDecision.NotifyActiveListUnavailable, $" Active pull request list unavailable ({reason}), so the pull request card was posted.");
    }

    /// <summary>
    /// When each pull request was announced, by pull request ID: the moment it became ready for review. When the table cannot be read, the list falls back
    /// to created dates rather than failing.
    /// </summary>
    private async Task<IReadOnlyDictionary<int, DateTimeOffset>> AnnouncedTimesAsync(
        string organization, IReadOnlyList<AzDoPullRequest> pullRequests, CancellationToken cancellationToken)
    {
        var idByKey = pullRequests.Where(pr => pr.RepositoryId is not null)
            .ToDictionary(pr => PullRequestCardFormatter.AnnounceOnceKey(pr.RepositoryId!, pr.Id), pr => pr.Id, StringComparer.Ordinal);
        try
        {
            var times = await announcementStore.GetAnnouncedTimesAsync(organization, idByKey.Keys, cancellationToken);
            return times.ToDictionary(t => idByKey[t.Key], t => t.Value);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogAnnouncementFailed(logger, NotificationTables.Announcement, ex.Message);
            return new Dictionary<int, DateTimeOffset>();
        }
    }

    private enum AnnouncementClaim
    {
        NotNeeded,
        Claimed,
        AlreadyAnnounced,
        Unavailable,
    }

    /// <summary>Claims before delivering, so simultaneous events cannot all post. When the table cannot be used, posts anyway: a duplicate card is better than a missed one.</summary>
    private async Task<AnnouncementClaim> ClaimAnnouncementAsync(NotificationSubject subject, string key, string correlationId, CancellationToken cancellationToken)
    {
        try
        {
            return await announcementStore.TryClaimAsync(subject.Organization, key, subject, correlationId, timeProvider.GetUtcNow(), cancellationToken)
                ? AnnouncementClaim.Claimed
                : AnnouncementClaim.AlreadyAnnounced;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogAnnouncementFailed(logger, NotificationTables.Announcement, ex.Message);
            return AnnouncementClaim.Unavailable;
        }
    }

    private async Task ReleaseAnnouncementAsync(NotificationSubject subject, string key, CancellationToken cancellationToken)
    {
        try
        {
            await announcementStore.ReleaseAsync(subject.Organization, key, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogAnnouncementFailed(logger, NotificationTables.Announcement, ex.Message);
        }
    }

    /// <summary>Maps workflow IDs to URLs. An unknown ID or non-https URL becomes a <see cref="NotificationOutcome.WorkflowNotConfigured"/> delivery.</summary>
    private async Task<(IReadOnlyList<(string Id, Uri? Url)> Targets, RoutingDecision Decision)> ResolveWorkflowsAsync(
        IReadOnlyList<string> workflowIds, RoutingDecision decision, CancellationToken cancellationToken)
    {
        var fallbackUrl = teamsWorkflowOptions.Value.FallbackWebhookUrl;
        IReadOnlyDictionary<string, string> urls = new Dictionary<string, string>();

        if (workflowIds.Any(id => !IsFallback(id)))
        {
            try
            {
                urls = await ruleStore.GetWorkflowUrlsAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogStoreUnreadable(logger, NotificationTables.Workflow, ex.Message);
                return ([(WorkflowIds.Fallback, fallbackUrl)], RoutingDecision.NotifyWorkflowsUnreadable);
            }
        }

        Uri? Resolve(string id)
        {
            var url = IsFallback(id) ? fallbackUrl : urls.TryGetValue(id, out var text) && Uri.TryCreate(text, UriKind.Absolute, out var parsed) ? parsed : null;
            return TeamsWorkflowOptions.IsHttpsUrl(url) ? url : null;
        }

        return (workflowIds.Distinct(StringComparer.OrdinalIgnoreCase).Select(id => (id, Resolve(id))).ToList(), decision);
    }

    private async Task<WorkflowDelivery> DeliverAsync(string workflowId, Uri? url, AdaptiveCard card, string correlationId, CancellationToken cancellationToken)
    {
        if (url is null)
        {
            return new WorkflowDelivery(workflowId, NotificationOutcome.WorkflowNotConfigured, null);
        }

        var submission = await teamsWorkflow.SubmitAsync(url, card, correlationId, cancellationToken);
        var outcome = submission.Status switch
        {
            WorkflowSubmissionStatus.Accepted => NotificationOutcome.WorkflowAccepted,
            WorkflowSubmissionStatus.Rejected => NotificationOutcome.WorkflowRejected,
            WorkflowSubmissionStatus.TimedOut => NotificationOutcome.WorkflowTimedOut,
            _ => NotificationOutcome.WorkflowUnreachable,
        };
        return new WorkflowDelivery(workflowId, outcome, submission.StatusCode);
    }

    private static string Describe(IReadOnlyList<WorkflowDelivery> deliveries)
    {
        var parts = deliveries.Select(d => d.Outcome switch
        {
            NotificationOutcome.WorkflowAccepted => $"Teams Workflow '{d.WorkflowId}' accepted the card (HTTP {d.StatusCode}).",
            NotificationOutcome.WorkflowRejected => $"Teams Workflow '{d.WorkflowId}' rejected the card (HTTP {d.StatusCode}).",
            NotificationOutcome.WorkflowTimedOut => $"Teams Workflow '{d.WorkflowId}' did not respond before the configured timeout.",
            NotificationOutcome.WorkflowNotConfigured => $"Teams Workflow '{d.WorkflowId}' has no https URL in the {NotificationTables.Workflow} table.",
            _ => $"Teams Workflow '{d.WorkflowId}' could not be reached.",
        });
        var asynchronous = deliveries.Any(d => d.Outcome == NotificationOutcome.WorkflowAccepted) ? " Workflows post to Teams asynchronously." : string.Empty;
        return string.Join(" ", parts) + asynchronous;
    }

    private static bool IsFallback(string workflowId) => workflowId.Equals(WorkflowIds.Fallback, StringComparison.OrdinalIgnoreCase);

    private NotificationResult Invalid(string detail)
    {
        LogNotDelivered(logger, NotificationOutcome.InvalidPayload, detail);
        return new NotificationResult(NotificationOutcome.InvalidPayload, detail, NotificationTrace.Empty);
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Received {EventType} event {EventId} (subscription {SubscriptionId}, notification {NotificationId}).")]
    private static partial void LogReceived(ILogger logger, string eventType, string? eventId, string? subscriptionId, int? notificationId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Notification not delivered: {Outcome}. {Detail}")]
    private static partial void LogNotDelivered(ILogger logger, NotificationOutcome outcome, string detail);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The {Table} table could not be read, so the fallback Teams Workflow is used: {Reason}")]
    private static partial void LogStoreUnreadable(ILogger logger, string table, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The {Table} table could not be used, so the card may post more than once: {Reason}")]
    private static partial void LogAnnouncementFailed(ILogger logger, string table, string reason);
}

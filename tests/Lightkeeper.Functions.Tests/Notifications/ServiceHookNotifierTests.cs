using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Lightkeeper.Client.AzDo;
using Lightkeeper.Functions.Cards;
using Lightkeeper.Functions.Integrations.TeamsWorkflow;
using Lightkeeper.Functions.Models;
using Lightkeeper.Functions.Notifications;
using Lightkeeper.Functions.Routing;
using Lightkeeper.Functions.Tests.TestSupport;
using static Lightkeeper.Functions.Tests.TestSupport.FakeNotificationRuleStore;

namespace Lightkeeper.Functions.Tests.Notifications;

/// <summary>The official sample is organization FabrikamFiber, project FabrikamFiberChat, folder \, pipeline "MainRepo (1)", result succeeded.</summary>
public sealed class ServiceHookNotifierTests
{
    private const string CorrelationId = "test-correlation";
    private static readonly Uri FallbackUrl = new("https://fallback.example.test/invoke?sig=f");
    private static readonly Uri PrdUrl = new("https://prd.example.test/invoke?sig=p");
    private static readonly Uri QaUrl = new("https://qa.example.test/invoke?sig=q");

    private static readonly NotificationRule IncludeAll = Rule("include-all", 100, RuleAction.Include);

    private readonly FakeNotificationRuleStore _store = new() { WorkflowUrls = { ["team-prd"] = PrdUrl.ToString(), ["team-qa"] = QaUrl.ToString() } };
    private readonly FakeTeamsWorkflowClient _workflow = new(FakeTeamsWorkflowClient.Accepted);
    private readonly FakeAzDoClient _azDo = new();

    [Fact]
    public async Task No_matching_rule_skips_without_posting()
    {
        _store.Rules.Add(Rule("other-project", 10, RuleAction.Include, project: "Fabrikam Fiber"));

        var result = await Notify(Samples.Read(Samples.BuildCompleted));

        Assert.Equal(NotificationOutcome.Skipped, result.Outcome);
        Assert.Equal(RoutingDecision.SkipNoRule, result.Trace.Decision);
        Assert.Null(result.Trace.Rule);
        Assert.Empty(_workflow.Submissions);
    }

    [Fact]
    public async Task Include_rule_without_workflow_sends_card_to_fallback_workflow_with_trace()
    {
        _store.Rules.Add(IncludeAll);

        var result = await Notify(Samples.Read(Samples.BuildCompleted));

        Assert.Equal(NotificationOutcome.WorkflowAccepted, result.Outcome);
        Assert.Contains("HTTP 202", result.Detail);
        var (url, card, correlationId) = Assert.Single(_workflow.Submissions);
        Assert.Equal(FallbackUrl, url);
        Assert.Equal("Build succeeded: MainRepo (1) · 20241202.1", card.FallbackText);
        Assert.Equal(CorrelationId, correlationId);
        Assert.Equal(RoutingDecision.Notify, result.Trace.Decision);
        Assert.Equal(IncludeAll.RuleId, result.Trace.Rule?.RuleId);
        Assert.Equal("FabrikamFiber", result.Trace.Subject?.Organization);
        Assert.Equal(@"\MainRepo (1)", result.Trace.Subject?.PipelinePath);
    }

    [Fact]
    public async Task Exclude_rule_skips_without_posting()
    {
        _store.Rules.Add(Rule("quiet", 10, RuleAction.Exclude, project: "FabrikamFiberChat"));

        var result = await Notify(Samples.Read(Samples.BuildCompleted));

        Assert.Equal(NotificationOutcome.Skipped, result.Outcome);
        Assert.Equal(RoutingDecision.Skip, result.Trace.Decision);
        Assert.Equal("quiet", result.Trace.Rule?.RuleId);
        Assert.Empty(_workflow.Submissions);
    }

    [Fact]
    public async Task Include_rule_posts_to_every_listed_workflow_in_order()
    {
        _store.Rules.Add(Rule("both", 10, RuleAction.Include, workflows: "team-prd; team-qa; fallback"));

        var result = await Notify(Samples.Read(Samples.BuildCompleted));

        Assert.Equal(NotificationOutcome.WorkflowAccepted, result.Outcome);
        Assert.Equal(["team-prd", "team-qa", "fallback"], result.Trace.Deliveries.Select(d => d.WorkflowId));
        Assert.Equivalent(new[] { PrdUrl, QaUrl, FallbackUrl }, _workflow.Submissions.Select(s => s.Url));
    }

    [Fact]
    public async Task One_failed_workflow_fails_the_request_but_others_still_post()
    {
        _store.Rules.Add(Rule("both", 10, RuleAction.Include, workflows: "team-prd;team-qa"));
        _workflow.RespondTo(QaUrl, new WorkflowSubmission(WorkflowSubmissionStatus.TimedOut));

        var result = await Notify(Samples.Read(Samples.BuildCompleted));

        Assert.Equal(NotificationOutcome.WorkflowTimedOut, result.Outcome);
        Assert.Equal([NotificationOutcome.WorkflowAccepted, NotificationOutcome.WorkflowTimedOut], result.Trace.Deliveries.Select(d => d.Outcome));
        Assert.Equal(2, _workflow.Submissions.Count);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("insecure")]
    public async Task Unknown_or_non_https_workflow_is_not_configured(string workflowId)
    {
        _store.WorkflowUrls["insecure"] = "http://insecure.example.test/invoke";
        _store.Rules.Add(Rule("r", 10, RuleAction.Include, workflows: workflowId));

        var result = await Notify(Samples.Read(Samples.BuildCompleted));

        Assert.Equal(NotificationOutcome.WorkflowNotConfigured, result.Outcome);
        Assert.Empty(_workflow.Submissions);
    }

    [Fact]
    public async Task Unreadable_rules_notify_fallback_workflow()
    {
        _store.RulesUnreadable = true;

        var result = await Notify(Samples.Read(Samples.BuildCompleted));

        Assert.Equal(NotificationOutcome.WorkflowAccepted, result.Outcome);
        Assert.Equal(RoutingDecision.NotifyRulesUnreadable, result.Trace.Decision);
        Assert.Equal(FallbackUrl, Assert.Single(_workflow.Submissions).Url);
    }

    [Fact]
    public async Task Unreadable_workflows_notify_fallback_workflow()
    {
        _store.WorkflowsUnreadable = true;
        _store.Rules.Add(Rule("r", 10, RuleAction.Include, workflows: "team-prd"));

        var result = await Notify(Samples.Read(Samples.BuildCompleted));

        Assert.Equal(RoutingDecision.NotifyWorkflowsUnreadable, result.Trace.Decision);
        Assert.Equal(FallbackUrl, Assert.Single(_workflow.Submissions).Url);
    }

    [Fact]
    public async Task Pull_request_is_announced_once_then_later_events_skip()
    {
        _store.Rules.Add(Rule("prs", 10, RuleAction.Include, trigger: "git.pullrequest.*", workflows: "team-prd"));
        var created = Samples.Read(Samples.PullRequestCreated);
        var updated = Samples.ReadNode(Samples.PullRequestCreated);
        updated["eventType"] = ServiceHookEventTypes.PullRequestUpdated;

        var first = await Notify(created);
        var second = await Notify(updated.ToJsonString());

        Assert.Equal(NotificationOutcome.WorkflowAccepted, first.Outcome);
        Assert.Equal(NotificationOutcome.Skipped, second.Outcome);
        Assert.Equal(RoutingDecision.SkipAlreadyAnnounced, second.Trace.Decision);
        Assert.Equal("prs", second.Trace.Rule?.RuleId);
        Assert.Equal(PrdUrl, Assert.Single(_workflow.Submissions).Url);
    }

    [Fact]
    public async Task Simultaneous_events_for_one_pull_request_post_once()
    {
        _store.Rules.Add(Rule("prs", 10, RuleAction.Include));
        var created = Samples.Read(Samples.PullRequestCreated);
        var updatedNode = Samples.ReadNode(Samples.PullRequestCreated);
        updatedNode["eventType"] = ServiceHookEventTypes.PullRequestUpdated;
        var updated = updatedNode.ToJsonString();

        var results = await Task.WhenAll(Notify(updated), Notify(updated), Notify(created));

        Assert.Single(_workflow.Submissions);
        Assert.Equal(2, results.Count(r => r.Trace.Decision == RoutingDecision.SkipAlreadyAnnounced));
    }

    [Fact]
    public async Task Draft_pull_request_skips_before_rules_and_is_announced_when_published()
    {
        _store.Rules.Add(Rule("prs", 10, RuleAction.Include));
        var node = Samples.ReadNode(Samples.PullRequestCreated);
        node["resource"]!["isDraft"] = true;

        var draft = await Notify(node.ToJsonString());
        node["eventType"] = ServiceHookEventTypes.PullRequestUpdated;
        node["resource"]!["isDraft"] = false;
        var published = await Notify(node.ToJsonString());

        Assert.Equal(RoutingDecision.SkipByEvent, draft.Trace.Decision);
        Assert.Equal(PullRequestCardFormatter.DraftSkipReason, draft.Trace.SkipReason);
        Assert.Null(draft.Trace.Rule);
        Assert.Equal(NotificationOutcome.WorkflowAccepted, published.Outcome);
        Assert.Single(_workflow.Submissions);
    }

    [Fact]
    public async Task Failed_delivery_does_not_record_announcement_so_next_event_retries()
    {
        _store.Rules.Add(Rule("prs", 10, RuleAction.Include));
        _workflow.RespondTo(FallbackUrl, new WorkflowSubmission(WorkflowSubmissionStatus.Rejected, 400));

        await Notify(Samples.Read(Samples.PullRequestCreated));

        Assert.Empty(_store.Announced);
    }

    [Fact]
    public async Task Unreadable_announcements_still_post()
    {
        _store.AnnouncementsUnreadable = true;
        _store.Rules.Add(Rule("prs", 10, RuleAction.Include));

        var result = await Notify(Samples.Read(Samples.PullRequestCreated));

        Assert.Equal(NotificationOutcome.WorkflowAccepted, result.Outcome);
    }

    [Fact]
    public async Task Active_list_rule_posts_the_list_instead_of_the_pull_request_card_once()
    {
        _store.Rules.Add(Rule("pr-list", 10, RuleAction.Include, trigger: "git.pullrequest.*", notificationType: NotificationType.ActiveList));
        _azDo.Result = new(AzDoResultStatus.Succeeded, [FakeAzDoClient.PullRequest(1, "Announced"), FakeAzDoClient.PullRequest(2, "Waiting"), FakeAzDoClient.PullRequest(3, "Draft", isDraft: true)]);

        var result = await Notify(Samples.Read(Samples.PullRequestCreated));
        var again = await Notify(Samples.Read(Samples.PullRequestCreated));

        Assert.Equal(NotificationOutcome.WorkflowAccepted, result.Outcome);
        Assert.Equal(RoutingDecision.Notify, result.Trace.Decision);
        Assert.Equal(("fabrikam", "Fabrikam"), Assert.Single(_azDo.Requests));
        Assert.Equal([1, 2], _azDo.CommentRequests.Order());
        var card = Assert.Single(_workflow.Submissions).Card;
        Assert.StartsWith(ActivePullRequestListCard.Title(2), card.FallbackText, StringComparison.Ordinal);
        Assert.Equal(2, card.Body.OfType<AdaptiveContainer>().Count(c => c.Items is [AdaptiveTextBlock, AdaptiveColumnSet]));
        Assert.Equal(RoutingDecision.SkipAlreadyAnnounced, again.Trace.Decision);
    }

    [Fact]
    public async Task Active_list_orders_and_dates_pull_requests_by_when_they_became_ready_for_review()
    {
        var published = new DateTimeOffset(2026, 11, 20, 15, 0, 0, TimeSpan.Zero);
        _store.Rules.Add(Rule("pr-list", 10, RuleAction.Include, trigger: "git.pullrequest.*", notificationType: NotificationType.ActiveList));
        _store.AnnouncedTimes[("fabrikam", PullRequestCardFormatter.AnnounceOnceKey("repo-id", 2))] = published;
        _azDo.Result = new(AzDoResultStatus.Succeeded,
        [
            FakeAzDoClient.PullRequest(1, "Announced") with { Created = published.AddDays(-60) },
            FakeAzDoClient.PullRequest(2, "Draft published") with { Created = published.AddDays(-40) },
            FakeAzDoClient.PullRequest(3, "Ready when created") with { Created = published.AddDays(-5) },
        ]);

        await Notify(Samples.Read(Samples.PullRequestCreated));

        var blocks = Assert.Single(_workflow.Submissions).Card.Body.OfType<AdaptiveContainer>().Where(c => c.Items is [AdaptiveTextBlock, AdaptiveColumnSet]).ToList();
        Assert.Equal(["!3", "!2", "!1"], blocks.Select(b => ((AdaptiveTextBlock)b.Items[0]).Text.Split("**")[1]));
        var facts = ((AdaptiveRichTextBlock)((AdaptiveColumnSet)blocks[1].Items[1]).Columns[0].Items[0]).Inlines;
        Assert.StartsWith($"👤 Jamal Hartnett · 📅 {CardStyle.LocalDate(published)}", string.Concat(facts.Select(r => r.Text)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Active_list_falls_back_to_created_dates_when_announcements_cannot_be_read()
    {
        _store.Rules.Add(Rule("pr-list", 10, RuleAction.Include, notificationType: NotificationType.ActiveList));
        _store.AnnouncementsUnreadable = true;
        _azDo.Result = new(AzDoResultStatus.Succeeded, [FakeAzDoClient.PullRequest(1, "Announced"), FakeAzDoClient.PullRequest(2, "Waiting")]);

        var result = await Notify(Samples.Read(Samples.PullRequestCreated));

        Assert.Equal(RoutingDecision.Notify, result.Trace.Decision);
        Assert.StartsWith(ActivePullRequestListCard.Title(2), Assert.Single(_workflow.Submissions).Card.FallbackText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Active_list_still_posts_when_comment_counts_are_unavailable()
    {
        _store.Rules.Add(Rule("pr-list", 10, RuleAction.Include, notificationType: NotificationType.ActiveList));
        _azDo.Result = new(AzDoResultStatus.Succeeded, [FakeAzDoClient.PullRequest(1, "Announced")]);
        _azDo.CommentResult = new(AzDoResultStatus.TimedOut);

        var result = await Notify(Samples.Read(Samples.PullRequestCreated));

        Assert.Equal(RoutingDecision.Notify, result.Trace.Decision);
        Assert.Contains("Comment counts unavailable for 1", result.Detail, StringComparison.Ordinal);
        Assert.IsType<AdaptiveContainer>(Assert.Single(_workflow.Submissions).Card.Body[1]);
    }

    [Theory]
    [InlineData(AzDoResultStatus.NotConfigured)]
    [InlineData(AzDoResultStatus.Rejected)]
    [InlineData(AzDoResultStatus.TimedOut)]
    [InlineData(AzDoResultStatus.Unreachable)]
    public async Task Active_list_unavailable_posts_the_pull_request_card_instead(AzDoResultStatus status)
    {
        _store.Rules.Add(Rule("pr-list", 10, RuleAction.Include, notificationType: NotificationType.ActiveList));
        _azDo.Result = new(status);

        var result = await Notify(Samples.Read(Samples.PullRequestCreated));

        Assert.Equal(NotificationOutcome.WorkflowAccepted, result.Outcome);
        Assert.Equal(RoutingDecision.NotifyActiveListUnavailable, result.Trace.Decision);
        Assert.Contains(status.ToString(), result.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("active pull request", Assert.Single(_workflow.Submissions).Card.FallbackText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Active_list_rule_on_a_build_posts_the_build_card_without_calling_azure_devops()
    {
        _store.Rules.Add(Rule("all", 10, RuleAction.Include, notificationType: NotificationType.ActiveList));

        var result = await Notify(Samples.Read(Samples.BuildCompleted));

        Assert.Equal(RoutingDecision.Notify, result.Trace.Decision);
        Assert.Empty(_azDo.Requests);
        Assert.StartsWith("Build succeeded", Assert.Single(_workflow.Submissions).Card.FallbackText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("""{"resource": {}}""")]
    [InlineData("""{"eventType": "build.complete", "resource": "text"}""")]
    [InlineData("""{"eventType": "build.complete", "resource": {"id": "not a number"}}""")]
    public async Task Malformed_payload_is_invalid_and_not_submitted(string payload)
    {
        var result = await Notify(payload);

        Assert.Equal(NotificationOutcome.InvalidPayload, result.Outcome);
        Assert.Empty(_workflow.Submissions);
    }

    [Fact]
    public async Task Event_without_its_own_card_gets_general_card_and_follows_rules()
    {
        _store.Rules.Add(Rule("pushes", 10, RuleAction.Include, trigger: "git.push"));
        var node = Samples.ReadNode(Samples.BuildCompleted);
        node["eventType"] = "git.push";

        var result = await Notify(node.ToJsonString());

        Assert.Equal(NotificationOutcome.WorkflowAccepted, result.Outcome);
        Assert.Equal("git.push", result.Trace.Subject?.Trigger);
        Assert.Equal("pushes", result.Trace.Rule?.RuleId);
    }

    [Fact]
    public async Task Event_without_its_own_card_and_no_rule_skips()
    {
        _store.Rules.Add(IncludeAll with { Trigger = "build.complete" });
        var node = Samples.ReadNode(Samples.BuildCompleted);
        node["eventType"] = "workitem.updated";

        var result = await Notify(node.ToJsonString());

        Assert.Equal(RoutingDecision.SkipNoRule, result.Trace.Decision);
        Assert.Empty(_workflow.Submissions);
    }

    [Theory]
    [InlineData(WorkflowSubmissionStatus.Rejected, NotificationOutcome.WorkflowRejected)]
    [InlineData(WorkflowSubmissionStatus.TimedOut, NotificationOutcome.WorkflowTimedOut)]
    [InlineData(WorkflowSubmissionStatus.Unreachable, NotificationOutcome.WorkflowUnreachable)]
    public async Task Downstream_failure_is_reported_as_failure(WorkflowSubmissionStatus status, NotificationOutcome expected)
    {
        _store.Rules.Add(IncludeAll);
        _workflow.RespondTo(FallbackUrl, new WorkflowSubmission(status));

        var result = await Notify(Samples.Read(Samples.BuildCompleted));

        Assert.Equal(expected, result.Outcome);
    }

    private Task<NotificationResult> Notify(string payload)
    {
        var options = Options.Create(new TeamsWorkflowOptions { FallbackWebhookUrl = FallbackUrl });
        IServiceHookCardFormatter[] formatters =
            [new BuildCompletedCardFormatter(), new PullRequestCardFormatter(ServiceHookEventTypes.PullRequestCreated), new PullRequestCardFormatter(ServiceHookEventTypes.PullRequestUpdated), new AnyEventCardFormatter()];
        var notifier = new ServiceHookNotifier(formatters, _store, _store, _workflow, options, _azDo, TimeProvider.System, NullLogger<ServiceHookNotifier>.Instance);
        return notifier.NotifyAsync(payload, CorrelationId, TestContext.Current.CancellationToken);
    }
}

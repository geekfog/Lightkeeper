using Lightkeeper.Functions.Notifications;
using Lightkeeper.Functions.Routing;
using static Lightkeeper.Functions.Tests.TestSupport.FakeNotificationRuleStore;

namespace Lightkeeper.Functions.Tests.Notifications;

public sealed class NotificationLogEntryTests
{
    private static readonly NotificationSubject Subject =
        new("fabrikam", "build.complete", new EventFacts("Fabrikam Fiber", @"\prd", "fabrikam-backend PRD", "Failed", "failed", "20261121.3"));

    [Fact]
    public void Workflow_and_outcome_columns_list_each_delivery_in_the_same_order()
    {
        var rule = Rule("prd-failures", 30, RuleAction.Include, workflows: "team-prd;team-qa");
        WorkflowDelivery[] deliveries =
        [
            new("team-prd", NotificationOutcome.WorkflowAccepted, 202),
            new("team-qa", NotificationOutcome.WorkflowRejected, 400),
        ];
        var result = new NotificationResult(NotificationOutcome.WorkflowRejected, "detail", new NotificationTrace(Subject, rule, RoutingDecision.Notify, deliveries));

        var entry = NotificationLogEntry.Create(result, "corr", "{}", DateTimeOffset.UnixEpoch, 502, TimeSpan.FromMilliseconds(42));

        Assert.Equal("team-prd; team-qa", entry.Workflow);
        Assert.Equal("WorkflowAccepted; WorkflowRejected", entry.Outcome);
        Assert.Equal(@"\prd\fabrikam-backend PRD", entry.Pipeline);
        Assert.Equal("Failed", entry.Status);
        Assert.Equal("prd-failures", entry.RuleId);
        Assert.Equal(30, entry.RulePriority);
        Assert.Equal("Include", entry.RuleAction);
        Assert.Equal("Notify (prd-failures)", entry.Decision);
        Assert.Equal(42, entry.DurationMs);
    }

    [Theory]
    [InlineData(RoutingDecision.Skip, "Skip (r1)")]
    [InlineData(RoutingDecision.SkipNoRule, "Skip (no rule)")]
    [InlineData(RoutingDecision.NotifyRulesUnreadable, "Notify (rules unreadable)")]
    [InlineData(RoutingDecision.NotifyWorkflowsUnreadable, "Notify (r1, workflows unreadable)")]
    [InlineData(RoutingDecision.SkipByEvent, "Skip (draft)")]
    [InlineData(RoutingDecision.SkipAlreadyAnnounced, "Skip (r1, already announced)")]
    [InlineData(RoutingDecision.None, "")]
    public void Decision_names_the_rule_that_made_it(RoutingDecision decision, string expected)
    {
        var rule = decision is RoutingDecision.SkipNoRule or RoutingDecision.NotifyRulesUnreadable or RoutingDecision.SkipByEvent or RoutingDecision.None
            ? null
            : Rule("r1", 1, RuleAction.Exclude);

        Assert.Equal(expected, NotificationLogEntry.DescribeDecision(decision, rule, "draft"));
    }

    [Fact]
    public void Request_that_never_reached_delivery_logs_its_overall_outcome()
    {
        var result = new NotificationResult(NotificationOutcome.InvalidPayload, "bad", NotificationTrace.Empty);

        var entry = NotificationLogEntry.Create(result, "corr", "not json", DateTimeOffset.UnixEpoch, 400, TimeSpan.Zero);

        Assert.Null(entry.Workflow);
        Assert.Equal("InvalidPayload", entry.Outcome);
        Assert.Equal(string.Empty, entry.Decision);
        Assert.Equal("not json", entry.Payload);
    }
}

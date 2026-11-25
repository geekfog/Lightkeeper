using Lightkeeper.Functions.Models;
using Lightkeeper.Functions.Routing;
using static Lightkeeper.Functions.Tests.TestSupport.FakeNotificationRuleStore;

namespace Lightkeeper.Functions.Tests.Routing;

/// <summary>Folder and pipeline paths are real ones from an Azure DevOps project, so the documented pattern semantics are checked on realistic names.</summary>
public sealed class RoutingTests
{
    [Theory]
    [InlineData(@"\prd", @"\prd", true)]
    [InlineData(@"\prd", @"\prd\infra", false)]
    [InlineData(@"\qa\**", @"\qa", true)]
    [InlineData(@"\qa\**", @"\qa\infra", true)]
    [InlineData(@"\qa\**", @"\qa2", false)]
    [InlineData(@"\qa*\**", @"\qa2\infra\destroy", true)]
    [InlineData(@"\qa?", @"\qa2", true)]
    [InlineData(@"\qa?", @"\qa", false)]
    [InlineData(@"\beta-*", @"\beta-uat", true)]
    [InlineData(@"\beta-*", @"\beta-dev\nested", false)]
    [InlineData(@"**\infra\**", @"\qa2\infra\destroy", true)]
    [InlineData(@"**\infra\**", @"\dev\infra", true)]
    [InlineData(@"**", @"\", true)]
    [InlineData(@"\", @"\", true)]
    [InlineData(@"\", @"\prd", false)]
    [InlineData(@"/QA/**", @"\qa\infra", true)]
    [InlineData(@"\PRD\", @"\prd", true)]
    public void Folder_patterns_follow_documented_semantics(string pattern, string folder, bool expected) =>
        Assert.Equal(expected, RulePattern.IsMatch(RulePattern.NormalizeFolder(pattern), folder));

    [Theory]
    [InlineData("*PR *", "[Pricing] PR Checks", true)]
    [InlineData("*PR *", "[beta-dev] PR Review Backend", true)]
    [InlineData("*PR *", "[Pricing] Release", false)]
    [InlineData("[Pricing] Release", "[pricing] release", true)]
    [InlineData("[beta-*] Deploy *", "[beta-qa] Deploy Admin FE", true)]
    [InlineData("*Playwright*", "playwright-tests-qat", true)]
    [InlineData("", "anything", true)]
    public void Pipeline_patterns_treat_brackets_literally_and_ignore_case(string pattern, string pipeline, bool expected) =>
        Assert.Equal(expected, RulePattern.IsMatch(pattern, pipeline));

    [Theory]
    [InlineData(null, "Stopped")]
    [InlineData("Failed", "Failed")]
    [InlineData("!Succeeded", "Partially Succeeded")]
    [InlineData("!Succeeded", "Failed")]
    [InlineData("!Succeeded", "Stopped")]
    public void Status_filter_matches(string? filter, string display) =>
        Assert.True(StatusFilter.IsMatch(filter, Facts(status: display)));

    [Theory]
    [InlineData(BuildResults.Succeeded)]
    [InlineData(BuildResults.PartiallySucceeded)]
    [InlineData(BuildResults.Failed)]
    [InlineData(BuildResults.Canceled)]
    public void Negated_status_equals_listing_the_others(string result)
    {
        var facts = Facts(status: BuildResults.DisplayName(result), statusValue: result);

        Assert.Equal(StatusFilter.IsMatch("!Succeeded", facts), StatusFilter.IsMatch("Partially Succeeded, Failed, Stopped", facts));
    }

    [Theory]
    [InlineData("Stopped")]
    [InlineData("canceled")]
    [InlineData("STOPPED; failed")]
    public void Status_entries_accept_display_names_or_event_values(string filter) =>
        Assert.True(StatusFilter.IsMatch(filter, Facts(status: "Stopped", statusValue: BuildResults.Canceled)));

    [Fact]
    public void Lowest_priority_number_wins_regardless_of_order()
    {
        var subject = Subject(folder: @"\Pricing", pipeline: "[Pricing] PR Checks", status: "Failed");
        NotificationRule[] rules =
        [
            Rule("all-failures", 900, RuleAction.Include, status: "!Succeeded"),
            Rule("pr-checks", 10, RuleAction.Exclude, folder: "**", pipeline: "*PR *"),
        ];

        Assert.Equal("pr-checks", NotificationRule.FirstMatch(rules, subject)?.RuleId);
    }

    [Fact]
    public void Equal_priorities_resolve_by_rule_id()
    {
        NotificationRule[] rules = [Rule("b", 5, RuleAction.Include), Rule("a", 5, RuleAction.Exclude)];

        Assert.Equal("a", NotificationRule.FirstMatch(rules, Subject())?.RuleId);
    }

    [Fact]
    public void Status_that_does_not_match_falls_through_to_the_next_rule()
    {
        var subject = Subject(folder: @"\prd", status: "Succeeded");
        NotificationRule[] rules = [Rule("prd-failures", 30, RuleAction.Include, folder: @"\prd", status: "!Succeeded"), Rule("opt-in", 9999, RuleAction.Exclude)];

        Assert.Equal("opt-in", NotificationRule.FirstMatch(rules, subject)?.RuleId);
    }

    [Theory]
    [InlineData("fabrikam", "Fabrikam Fiber", "build.complete", true)]
    [InlineData("FABRIKAM", "fabrikam fiber", "BUILD.COMPLETE", true)]
    [InlineData("contoso", "Fabrikam Fiber", "build.complete", false)]
    [InlineData("fabrikam", "Dev", "build.complete", false)]
    [InlineData("fabrikam", "Fabrikam Fiber", "git.push", false)]
    [InlineData("*", null, null, true)]
    public void Organization_project_and_trigger_scope_a_rule(string organization, string? project, string? trigger, bool expected)
    {
        var rule = Rule("r", 1, RuleAction.Include, organization: organization, project: project, trigger: trigger);

        Assert.Equal(expected, rule.AppliesTo(Subject()));
    }

    [Theory]
    [InlineData("https://dev.azure.com/fabrikam/", "fabrikam")]
    [InlineData("https://dev.azure.com/Fabrikam%20Fiber/", "Fabrikam Fiber")]
    [InlineData("https://fabrikam.visualstudio.com/", "fabrikam")]
    [InlineData("https://example.test/fabrikam/", null)]
    [InlineData(null, null)]
    public void Organization_is_read_from_the_base_url(string? baseUrl, string? expected) =>
        Assert.Equal(expected, AzureDevOpsOrganization.FromBaseUrl(baseUrl));

    private static EventFacts Facts(string folder = @"\", string pipeline = "Pipeline", string status = "Succeeded", string? statusValue = null) =>
        new("Fabrikam Fiber", folder, pipeline, status, statusValue ?? status.Replace(" ", string.Empty, StringComparison.Ordinal), "1");

    private static NotificationSubject Subject(string folder = @"\", string pipeline = "Pipeline", string status = "Succeeded") =>
        new("fabrikam", "build.complete", Facts(folder, pipeline, status));
}

using System.Text.Json.Nodes;
using Lightkeeper.Functions.Cards;
using Lightkeeper.Functions.Models;
using Lightkeeper.Functions.Tests.TestSupport;
using Facts = Lightkeeper.Functions.Cards.PullRequestCardFormatter.FactNames;

namespace Lightkeeper.Functions.Tests.Cards;

/// <summary>The official samples are repository Fabrikam, pull request 1 from mytopic into main, by Jamal Hartnett; created is active, updated is completed.</summary>
public sealed class PullRequestCardFormatterTests
{
    private readonly PullRequestCardFormatter _formatter = new(ServiceHookEventTypes.PullRequestCreated);

    [Fact]
    public void Official_created_sample_maps_to_review_card_with_rule_facts_and_once_key()
    {
        var result = _formatter.Format(Samples.ReadEvent(Samples.PullRequestCreated));
        var card = result.Card!;

        var header = Assert.IsType<AdaptiveContainer>(card.Body[0]);
        Assert.Equal(CardStyle.Review.ContainerStyle, header.Style);
        Assert.Equal(
            ["👀 my first pull request", "Ready for review · Fabrikam · mytopic → main", "🗓️ Created {{DATE(2024-06-17T11:22:33Z, SHORT)}} {{TIME(2024-06-17T11:22:33Z)}}"],
            header.Items.Cast<AdaptiveTextBlock>().Select(t => t.Text));
        Assert.Equal(
            new Dictionary<string, string>
            {
                [Facts.Project] = "Fabrikam",
                [Facts.PullRequest] = "!1",
                [Facts.Author] = "Jamal Hartnett",
                [Facts.Reviewers] = @"[Mobile]\Mobile Team",
            },
            Assert.IsType<AdaptiveFactSet>(card.Body[1]).Facts.ToDictionary(f => f.Title, f => f.Value));
        Assert.Equal("- test2", Assert.IsType<AdaptiveTextBlock>(card.Body[2]).Text);
        Assert.Equal(
            new AdaptiveOpenUrlAction(PullRequestCardFormatter.ReviewLinkName, "https://dev.azure.com/fabrikam/DefaultCollection/_git/Fabrikam/pullrequest/1"),
            Assert.Single(card.Actions));

        Assert.Equal("Fabrikam", result.Facts!.Project);
        Assert.Equal(@"\main", result.Facts.Folder);
        Assert.Equal("Fabrikam", result.Facts.Pipeline);
        Assert.Equal("Active", result.Facts.Status);
        Assert.Equal("Fabrikam · mytopic → main", result.Facts.Path);
        Assert.Equal("pullrequest-b1b1b1b1-cccc-dddd-eeee-f2f2f2f2f2f2-1", result.AnnounceOnceKey);
    }

    [Fact]
    public void Official_updated_sample_is_completed_so_it_skips()
    {
        var result = new PullRequestCardFormatter(ServiceHookEventTypes.PullRequestUpdated).Format(Samples.ReadEvent(Samples.PullRequestUpdated));

        Assert.Null(result.Card);
        Assert.Equal("completed", result.SkipReason);
        Assert.Equal("Completed", result.Facts!.Status);
    }

    [Fact]
    public void Draft_skips_with_draft_status_for_rules()
    {
        var result = _formatter.Format(WithResource(r => r["isDraft"] = true));

        Assert.Null(result.Card);
        Assert.Equal(PullRequestCardFormatter.DraftSkipReason, result.SkipReason);
        Assert.Equal(PullRequestStatuses.Draft, result.Facts!.Status);
    }

    [Fact]
    public void Web_link_in_event_wins_over_derived_link()
    {
        var card = _formatter.Format(WithResource(r => r["_links"] = new JsonObject { ["web"] = new JsonObject { ["href"] = "https://example.test/pr/1" } })).Card!;

        Assert.Equal("https://example.test/pr/1", Assert.Single(card.Actions).Url);
    }

    [Fact]
    public void Required_reviewers_come_first_and_are_marked() =>
        Assert.Equal(
            "Ana (required), Ben",
            PullRequestCardFormatter.Reviewers([new PullRequestReviewer("Ben", 0, false), new PullRequestReviewer("Ana", 0, true), new PullRequestReviewer(" ", 0, null)]));

    [Theory]
    [InlineData("pullRequestId", "resource.pullRequestId is required.")]
    [InlineData("title", "resource.title is required.")]
    [InlineData("status", "resource.status is required.")]
    [InlineData("repository", "resource.repository.name is required.")]
    public void Missing_required_field_is_reported(string field, string error)
    {
        var result = _formatter.Format(WithResource(r => r.Remove(field)));

        Assert.Null(result.Card);
        Assert.Equal([error], result.Errors);
    }

    [Theory]
    [InlineData("  one\r\n two  ", 50, "one two")]
    [InlineData("abcdefghij", 6, "abcde…")]
    [InlineData(" \r\n ", 10, null)]
    [InlineData("## Issue\r\n> quoted\r\nbody #42", 50, "Issue quoted body #42")]
    public void Excerpt_collapses_whitespace_and_cuts_long_text(string text, int max, string? expected) => Assert.Equal(expected, CardStyle.Excerpt(text, max));

    [Theory]
    [InlineData("## Issue\r\n\r\n- **one**\r\n- [two](https://x.test)", 100, "**Issue**\n\n- **one**\n- [two](https://x.test)")]
    [InlineData("line one\nline two\nline three", 18, "line one\nline two…")]
    [InlineData("abcdefghij", 6, "abcde…")]
    [InlineData(" \r\n ", 10, null)]
    public void Markdown_excerpt_keeps_lines_bolds_headings_and_cuts_on_line_boundaries(string text, int max, string? expected) =>
        Assert.Equal(expected, CardStyle.MarkdownExcerpt(text, max));

    private static ServiceHookEvent WithResource(Action<JsonObject> change)
    {
        var node = Samples.ReadNode(Samples.PullRequestCreated);
        change(node["resource"]!.AsObject());
        return Samples.Parse(node);
    }
}

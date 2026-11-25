using Lightkeeper.Client.AzDo;
using Lightkeeper.Functions.Cards;
using Lightkeeper.Functions.Models;
using Lightkeeper.Functions.Routing;
using Lightkeeper.Functions.Tests.TestSupport;

namespace Lightkeeper.Functions.Tests.Cards;

public sealed class ActivePullRequestListCardTests
{
    private static readonly NotificationSubject Subject = new(
        "fabrikam",
        ServiceHookEventTypes.PullRequestCreated,
        new EventFacts("Fabrikam", @"\main", "Fabrikam", "Active", "active", "!2", "Fabrikam · feature → main"));

    private static readonly DateTimeOffset Now = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);

    private static readonly IReadOnlyDictionary<int, DateTimeOffset> NoAnnouncements = new Dictionary<int, DateTimeOffset>();

    [Theory]
    [InlineData("2026-04-24", "6M, 30d", 151)]
    [InlineData("2026-10-23", "1M", 21)]
    [InlineData("2025-11-22", "1Y, 1d", 261)]
    [InlineData("2025-09-20", "1Y, 2M, 3d", 306)]
    [InlineData("2024-11-23", "2Y", 521)]
    [InlineData("2026-11-18", "5d", 3)]
    [InlineData("2026-11-19", "4d", 2)]
    [InlineData("2026-11-20", "3d", 1)]
    [InlineData("2026-11-21", "2d", 1)]
    [InlineData("2026-11-23", "", 0)]
    [InlineData("2026-11-24", "", 0)]
    public void Age_is_whole_months_and_days_and_overdue_after_two_business_days(string created, string expected, int businessDays)
    {
        // Now is Monday, November 23, 2026.
        var age = ActivePullRequestListCard.AgeOf(DateTimeOffset.Parse(created + "T08:00:00Z", System.Globalization.CultureInfo.InvariantCulture), Now);

        Assert.Equal(expected, age.ToString());
        Assert.Equal(businessDays, age.BusinessDays);
        Assert.Equal(businessDays > ActivePullRequestListCard.OverdueAfterBusinessDays, age.IsOverdue);
    }

    [Fact]
    public void Lists_each_pull_request_with_a_review_button_and_stars_the_announced_one()
    {
        var created = new DateTimeOffset(2026, 11, 4, 15, 0, 0, TimeSpan.Zero);
        var reviewers = new AzDoReviewer(@"[Fabrikam]\Code Reviewers", true, true, ["Ana Diaz"]);
        var card = ActivePullRequestListCard.Create(
            Subject,
            [FakeAzDoClient.PullRequest(1, "Fix [login]", false, reviewers) with { Created = created }, FakeAzDoClient.PullRequest(2, "Announced")],
            new Dictionary<int, AzDoCommentCount> { [1] = new(1, 1) },
            NoAnnouncements,
            Now);

        Assert.Equal("👀 2 active pull requests", Text(((AdaptiveContainer)card.Body[0]).Items[0]));
        Assert.Equal("2 active pull requests: Ready for review: !2 Announced", card.FallbackText);

        var (details, review) = Block(card.Body[1]);
        Assert.Equal(ActivePullRequestListCard.CompletionMarker + " **!1** [Fix (login)](https://dev.azure.com/fabrikam/Fabrikam/_git/Fabrikam/pullrequest/1)", Text(details[0]));
        var facts = Assert.IsType<AdaptiveRichTextBlock>(details[1]).Inlines;
        Assert.Equal($"👤 Jamal Hartnett · 📅 {CardStyle.LocalDate(created)} (19d) 🌿 Fabrikam · feature → main", string.Concat(facts.Select(r => r.Text)));
        var red = Assert.Single(facts, r => r.Color is not null);
        Assert.Equal(("(19d)", CardStyle.Failure.TextColor), (red.Text, red.Color));
        Assert.Equal(ActivePullRequestListCard.CommentsMarker + " 1/1" + ActivePullRequestListCard.CommentsSuffix, RichText(details[2]));
        Assert.Equal(ActivePullRequestListCard.RequiredReviewersPrefix + "✅ Code Reviewers (Ana Diaz)", Text(details[3]));
        Assert.Equal((ActivePullRequestListCard.ReviewButtonName, "https://dev.azure.com/fabrikam/Fabrikam/_git/Fabrikam/pullrequest/1"), (review.Title, review.Url));

        // The announced pull request shows Now; no comments or required reviewers leave those lines out rather than leaving them blank.
        var (announced, _) = Block(card.Body[2]);
        Assert.StartsWith(ActivePullRequestListCard.AnnouncedMarker + " **!2**", Text(announced[0]), StringComparison.Ordinal);
        Assert.Equal(2, announced.Count);
        Assert.Equal("👤 Jamal Hartnett · 📅 Now 🌿 Fabrikam · feature → main", RichText(announced[1]));

        Assert.Empty(card.Actions);
    }

    [Theory]
    [InlineData(false, true, true, 5, ActivePullRequestListCard.CompletionMarker)]
    [InlineData(true, true, true, null, ActivePullRequestListCard.CompletionMarker)]
    [InlineData(false, true, false, 5, ActivePullRequestListCard.OverdueMarker)]
    [InlineData(false, false, false, 5, ActivePullRequestListCard.OverdueMarker)]
    [InlineData(false, true, false, 1, ActivePullRequestListCard.NeedsApprovalMarker)]
    [InlineData(false, false, false, null, ActivePullRequestListCard.NeedsApprovalMarker)]
    [InlineData(true, true, false, null, null)]
    [InlineData(true, false, false, null, null)]
    public void Status_marker_shows_approved_then_overdue_then_needs_approval_and_nothing_extra_for_the_announced_one(
        bool isAnnounced, bool hasRequiredReviewer, bool approved, int? businessDays, string? expected)
    {
        var reviewers = hasRequiredReviewer ? new[] { new AzDoReviewer("Ana Diaz", false, approved, []), new AzDoReviewer("Lee Park", false, true, []) } : [];
        ActivePullRequestListCard.Age? age = businessDays is { } days ? new(0, 0, days, days, days) : null;

        Assert.Equal(expected, ActivePullRequestListCard.StatusMarker(FakeAzDoClient.PullRequest(1, "PR", false, reviewers), isAnnounced, age));
    }

    [Fact]
    public void Title_line_puts_the_star_before_the_status_marker()
    {
        var approved = new AzDoReviewer("Ana Diaz", false, true, []);
        var card = ActivePullRequestListCard.Create(Subject, [FakeAzDoClient.PullRequest(2, "Announced", false, approved)], new Dictionary<int, AzDoCommentCount>(), NoAnnouncements, Now);

        var (announced, _) = Block(card.Body[1]);
        Assert.StartsWith($"{ActivePullRequestListCard.AnnouncedMarker} {ActivePullRequestListCard.CompletionMarker} **!2**", Text(announced[0]), StringComparison.Ordinal);
    }

    [Fact]
    public void Overdue_pull_request_needing_approval_is_marked_and_a_recent_one_is_marked_for_review()
    {
        var card = ActivePullRequestListCard.Create(
            Subject with { Facts = Subject.Facts with { Reference = "!9" } },
            [FakeAzDoClient.PullRequest(1, "Old") with { Created = Now.AddDays(-30) }, FakeAzDoClient.PullRequest(2, "Recent") with { Created = Now.AddDays(-1) }],
            new Dictionary<int, AzDoCommentCount>(),
            NoAnnouncements,
            Now);

        Assert.StartsWith(ActivePullRequestListCard.OverdueMarker + " **!1**", Text(Block(card.Body[1]).Details[0]), StringComparison.Ordinal);
        Assert.StartsWith(ActivePullRequestListCard.NeedsApprovalMarker + " **!2**", Text(Block(card.Body[2]).Details[0]), StringComparison.Ordinal);
    }

    [Fact]
    public void Announced_pull_request_shows_now_and_one_created_the_same_day_shows_its_date_with_no_age()
    {
        var earlier = Now.AddHours(-3);
        var card = ActivePullRequestListCard.Create(
            Subject,
            [FakeAzDoClient.PullRequest(1, "Earlier") with { Created = earlier }, FakeAzDoClient.PullRequest(2, "Announced") with { Created = Now.AddDays(-30) }],
            new Dictionary<int, AzDoCommentCount>(),
            NoAnnouncements,
            Now);

        Assert.Equal($"👤 Jamal Hartnett · 📅 {CardStyle.LocalDate(earlier)} 🌿 Fabrikam · feature → main", FactsText(card.Body[1]));
        Assert.Equal($"👤 Jamal Hartnett · 📅 {ActivePullRequestListCard.Now} 🌿 Fabrikam · feature → main", FactsText(card.Body[2]));
    }

    [Fact]
    public void Full_list_with_long_titles_stays_under_the_Teams_message_size_limit()
    {
        const int TeamsMessageLimitBytes = 28 * 1024;
        var reviewers = new[] { new AzDoReviewer(@"[Fabrikam]\Code Reviewers", true, true, ["Ana Diaz", "Lee Park"]), new AzDoReviewer("Jamal Hartnett", false, false, []) };
        var reviewable = Enumerable.Range(1, ActivePullRequestListCard.MaxListed)
            .Select(i => FakeAzDoClient.PullRequest(i, new string('x', 120), false, reviewers) with { Created = DateTimeOffset.UnixEpoch })
            .ToList();
        var comments = reviewable.ToDictionary(pr => pr.Id, _ => new AzDoCommentCount(3, 10));

        var json = System.Text.Json.JsonSerializer.Serialize(ActivePullRequestListCard.Create(Subject, reviewable, comments, NoAnnouncements, Now), LightkeeperJson.Options);

        Assert.True(System.Text.Encoding.UTF8.GetByteCount(json) < TeamsMessageLimitBytes, $"{System.Text.Encoding.UTF8.GetByteCount(json)} bytes");
    }

    [Fact]
    public void Reviewable_leaves_out_drafts() =>
        Assert.Equal([1], ActivePullRequestListCard.Reviewable([FakeAzDoClient.PullRequest(1, "Ready"), FakeAzDoClient.PullRequest(2, "Draft", isDraft: true)], NoAnnouncements).Select(pr => pr.Id));

    [Fact]
    public void Ready_for_review_date_orders_and_dates_a_pull_request_in_place_of_its_created_date()
    {
        var published = Now.AddDays(-1);
        var announcedAt = new Dictionary<int, DateTimeOffset> { [1] = published };
        var reviewable = ActivePullRequestListCard.Reviewable(
            [FakeAzDoClient.PullRequest(1, "Draft published yesterday") with { Created = Now.AddDays(-40) }, FakeAzDoClient.PullRequest(2, "Ready when created") with { Created = Now.AddDays(-5) }],
            announcedAt);

        var card = ActivePullRequestListCard.Create(Subject with { Facts = Subject.Facts with { Reference = "!9" } }, reviewable, new Dictionary<int, AzDoCommentCount>(), announcedAt, Now);

        Assert.Equal([2, 1], reviewable.Select(pr => pr.Id));
        Assert.Equal($"👤 Jamal Hartnett · 📅 {CardStyle.LocalDate(published)} (1d) 🌿 Fabrikam · feature → main", FactsText(card.Body[2]));
    }

    [Fact]
    public void Announced_pull_request_is_last_even_when_created_before_the_others()
    {
        var reviewable = Enumerable.Range(1, 4).Select(i => FakeAzDoClient.PullRequest(i, "PR")).ToList();

        var shown = ActivePullRequestListCard.Shown(reviewable, PullRequestCardFormatter.Reference(2));

        Assert.Equal([1, 3, 4, 2], shown.Select(pr => pr.Id));
    }

    [Fact]
    public void Long_list_is_cut_but_keeps_the_announced_pull_request_with_a_count_of_the_rest()
    {
        var count = ActivePullRequestListCard.MaxListed + 3;
        var reviewable = Enumerable.Range(1, count).Select(i => FakeAzDoClient.PullRequest(i, "PR")).ToList();
        var subject = Subject with { Facts = Subject.Facts with { Reference = PullRequestCardFormatter.Reference(count) } };

        var shown = ActivePullRequestListCard.Shown(reviewable, subject.Facts.Reference);
        var card = ActivePullRequestListCard.Create(subject, reviewable, new Dictionary<int, AzDoCommentCount>(), NoAnnouncements, Now);

        Assert.Equal(ActivePullRequestListCard.MaxListed, shown.Count);
        Assert.Equal(count, shown[^1].Id);
        Assert.Equal("…and 3 more", Text(card.Body[^1]));
    }

    [Theory]
    [InlineData(1, 3, " 1/3", "attention")]
    [InlineData(2, 2, " 2/2", "good")]
    public void Comments_show_resolved_of_total_with_a_green_check_when_all_are_resolved_and_red_otherwise(int resolved, int total, string expected, string? color)
    {
        var run = Assert.Single(ActivePullRequestListCard.CommentsLine(new AzDoCommentCount(resolved, total), hasConflicts: false)!.Inlines);

        Assert.Equal(ActivePullRequestListCard.CommentsMarker + expected + ActivePullRequestListCard.CommentsSuffix, run.Text);
        Assert.Equal(color, run.Color);
    }

    [Fact]
    public void Merge_conflicts_show_in_red_after_the_comment_count_or_alone()
    {
        var withComments = ActivePullRequestListCard.CommentsLine(new AzDoCommentCount(2, 2), hasConflicts: true)!.Inlines;
        var alone = Assert.Single(ActivePullRequestListCard.CommentsLine(null, hasConflicts: true)!.Inlines);

        Assert.Equal($"{ActivePullRequestListCard.CommentsMarker} 2/2{ActivePullRequestListCard.CommentsSuffix} · {ActivePullRequestListCard.ConflictsText}", string.Concat(withComments.Select(r => r.Text)));
        Assert.Equal((ActivePullRequestListCard.ConflictsText, CardStyle.Failure.TextColor), (withComments[^1].Text, withComments[^1].Color));
        Assert.Equal((ActivePullRequestListCard.ConflictsText, CardStyle.Failure.TextColor), (alone.Text, alone.Color));
    }

    [Fact]
    public void No_comments_or_unknown_count_and_no_conflicts_has_no_comment_line()
    {
        Assert.Null(ActivePullRequestListCard.CommentsLine(new AzDoCommentCount(0, 0), hasConflicts: false));
        Assert.Null(ActivePullRequestListCard.CommentsLine(null, hasConflicts: false));
    }

    [Theory]
    [InlineData(@"[Fabrikam]\Code Reviewers", true, true, "Ana Diaz", "✅ Code Reviewers (Ana Diaz)")]
    [InlineData(@"[Fabrikam]\Code Reviewers", true, false, null, "Code Reviewers")]
    [InlineData("Jamal Hartnett", false, true, null, "✅ Jamal Hartnett")]
    [InlineData("Jamal Hartnett", false, false, null, "Jamal Hartnett")]
    public void Required_reviewers_are_checked_when_approved_with_who_approved_for_a_group(string name, bool isGroup, bool approved, string? by, string expected) =>
        Assert.Equal(expected, ActivePullRequestListCard.Reviewer(new AzDoReviewer(name, isGroup, approved, by is null ? [] : [by])));

    private static string Text(AdaptiveElement element) => Assert.IsType<AdaptiveTextBlock>(element).Text;

    private static string RichText(AdaptiveElement element) => string.Concat(Assert.IsType<AdaptiveRichTextBlock>(element).Inlines.Select(r => r.Text));

    private static string FactsText(AdaptiveElement block) => RichText(Block(block).Details[1]);

    /// <summary>The title, which spans the block, followed by the lines beside the review button.</summary>
    private static (IReadOnlyList<AdaptiveElement> Details, AdaptiveOpenUrlAction Review) Block(AdaptiveElement element)
    {
        var block = Assert.IsType<AdaptiveContainer>(element);
        Assert.Null(block.Style);
        Assert.True(block.Separator);
        Assert.Equal(2, block.Items.Count);
        var columns = Assert.IsType<AdaptiveColumnSet>(block.Items[1]).Columns;
        return ([block.Items[0], .. columns[0].Items], Assert.Single(Assert.IsType<AdaptiveActionSet>(Assert.Single(columns[1].Items)).Actions));
    }
}

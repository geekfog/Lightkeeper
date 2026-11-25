using System.Globalization;
using Lightkeeper.Client.AzDo;
using Lightkeeper.Functions.Routing;

namespace Lightkeeper.Functions.Cards;

/// <summary>
/// What an <see cref="NotificationType.ActiveList"/> rule posts instead of the pull request card: the project's active pull requests that are not drafts,
/// oldest first, one block each, with the one being announced starred and last. Blocks rather than a table, so the card reads at any width.
/// </summary>
public static class ActivePullRequestListCard
{
    public const string ReviewButtonName = "Review PR";
    public const string ApprovedMarker = "✅";
    public const string AuthorMarker = "👤";
    public const string ReadyMarker = "📅";
    public const string RepositoryMarker = "🌿";

    /// <summary>In place of the date and age of the pull request being announced, which has just become ready for review.</summary>
    public const string Now = "Now";

    /// <summary>A text check mark (the variation selector stops it rendering as a fixed-color emoji), colored by <see cref="CommentsLine"/>.</summary>
    public const string CommentsMarker = "\u2714\uFE0E";

    /// <summary>In front of the pull request being announced.</summary>
    public const string AnnouncedMarker = "⭐";

    /// <summary>In front of a pull request every required reviewer approved, so it only needs completing.</summary>
    public const string CompletionMarker = "🏁";

    /// <summary>In front of an overdue pull request (see <see cref="OverdueAfterBusinessDays"/>) that still needs an approval.</summary>
    public const string OverdueMarker = "🙈";

    /// <summary>In front of any other pull request that still needs an approval, other than the one being announced.</summary>
    public const string NeedsApprovalMarker = "🔍";

    /// <summary>A pull request waiting more than this many business days (Monday to Friday) shows its age in red.</summary>
    public const int OverdueAfterBusinessDays = 2;

    public const string CommentsSuffix = " comments resolved";
    public const string ConflictsText = "Merge conflicts";
    public const string RequiredReviewersPrefix = "Required: ";

    /// <summary>Keeps the card under the Teams message size limit; each row also costs one comment thread lookup.</summary>
    public const int MaxListed = 15;

    private const string ItemSeparator = " · ";
    private const string Space = " ";

    /// <summary>Unresolved comments, merge conflicts, and an overdue age.</summary>
    private static readonly string AlertColor = CardStyle.Failure.TextColor;
    private const string AgeSeparator = ", ";
    private const string BranchArrow = " → ";
    private const string NameSeparator = ", ";
    private const char GroupScopeSeparator = '\\';

    /// <summary>Active pull requests worth reviewing, oldest <see cref="ReadySince"/> first: drafts are left out.</summary>
    /// <param name="announcedAt">When each pull request was announced, by ID.</param>
    public static IReadOnlyList<AzDoPullRequest> Reviewable(IReadOnlyList<AzDoPullRequest> pullRequests, IReadOnlyDictionary<int, DateTimeOffset> announcedAt) =>
        [.. pullRequests.Where(pr => !pr.IsDraft).OrderBy(pr => ReadySince(pr, announcedAt) ?? DateTimeOffset.MaxValue).ThenBy(pr => pr.Id)];

    /// <summary>
    /// When the pull request became ready for review: when it was announced, since a draft is announced only once published; otherwise, such as for one
    /// published before announcements were recorded, when it was created.
    /// </summary>
    public static DateTimeOffset? ReadySince(AzDoPullRequest pr, IReadOnlyDictionary<int, DateTimeOffset> announcedAt) =>
        announcedAt.TryGetValue(pr.Id, out var announced) ? announced : pr.Created;

    /// <summary>
    /// The rows the card shows, at most <see cref="MaxListed"/>: the others oldest first, then the announced pull request last, since it has only just become
    /// ready for review even when it was created earlier as a draft.
    /// </summary>
    public static IReadOnlyList<AzDoPullRequest> Shown(IReadOnlyList<AzDoPullRequest> reviewable, string? announcedReference)
    {
        if (reviewable.FirstOrDefault(pr => IsAnnounced(pr, announcedReference)) is not { } announced) return [.. reviewable.Take(MaxListed)];
        return [.. reviewable.Where(pr => pr != announced).Take(MaxListed - 1), announced];
    }

    /// <param name="reviewable">From <see cref="Reviewable"/>.</param>
    /// <param name="comments">Comment counts by pull request ID; a pull request without one shows no comment line.</param>
    /// <param name="announcedAt">When each pull request was announced, by ID; see <see cref="ReadySince"/>.</param>
    /// <param name="now">What each pull request's age is measured to.</param>
    public static AdaptiveCard Create(
        NotificationSubject subject,
        IReadOnlyList<AzDoPullRequest> reviewable,
        IReadOnlyDictionary<int, AzDoCommentCount> comments,
        IReadOnlyDictionary<int, DateTimeOffset> announcedAt,
        DateTimeOffset now)
    {
        var reference = subject.Facts.Reference;
        var announced = reviewable.FirstOrDefault(pr => IsAnnounced(pr, reference));
        var shown = Shown(reviewable, reference);
        var title = Title(reviewable.Count);
        var subtitle = $"{PullRequestCardFormatter.ReadyForReview}: {reference} {announced?.Title}".TrimEnd();

        List<AdaptiveElement> body =
        [
            CardStyle.Header(CardStyle.Review, title, subtitle),
            .. shown.Select(pr => Block(pr, comments.GetValueOrDefault(pr.Id), ReadySince(pr, announcedAt), pr == announced, now)),
        ];
        if (reviewable.Count > shown.Count)
        {
            body.Add(new AdaptiveTextBlock(string.Create(CultureInfo.InvariantCulture, $"…and {reviewable.Count - shown.Count} more")) { IsSubtle = true });
        }

        return new AdaptiveCard
        {
            FallbackText = $"{title}: {subtitle}",
            Body = body,
        };
    }

    internal static string Title(int count) => count == 1 ? "1 active pull request" : string.Create(CultureInfo.InvariantCulture, $"{count} active pull requests");

    /// <summary>
    /// Resolved out of total comment threads, such as <c>✔ 2/2 comments resolved</c>, green when all are resolved and red otherwise, then
    /// <see cref="ConflictsText"/> in red when the pull request has merge conflicts; null when there is neither.
    /// </summary>
    /// <param name="count">Null or zero total shows no comment count.</param>
    internal static AdaptiveRichTextBlock? CommentsLine(AzDoCommentCount? count, bool hasConflicts)
    {
        List<AdaptiveTextRun> runs = [];
        if (count is { Total: > 0 })
        {
            runs.Add(new(string.Create(CultureInfo.InvariantCulture, $"{CommentsMarker} {count.Resolved}/{count.Total}{CommentsSuffix}"))
            {
                Color = count.Resolved == count.Total ? CardStyle.Success.TextColor : AlertColor,
            });
        }

        if (hasConflicts)
        {
            if (runs.Count > 0) runs.Add(new(ItemSeparator) { IsSubtle = true });
            runs.Add(new(ConflictsText) { Color = AlertColor });
        }

        return runs.Count == 0 ? null : new AdaptiveRichTextBlock(runs) { Spacing = "None" };
    }

    /// <summary>Such as <c>✅ Code Reviewers (Jamal Hartnett)</c> for a group a member approved for, or <c>Jamal Hartnett</c> for a user yet to approve.</summary>
    internal static string Reviewer(AzDoReviewer reviewer)
    {
        var name = reviewer.IsGroup ? reviewer.Name[(reviewer.Name.LastIndexOf(GroupScopeSeparator) + 1)..] : reviewer.Name;
        var by = reviewer.ApprovedBy.Count == 0 ? string.Empty : $" ({string.Join(NameSeparator, reviewer.ApprovedBy)})";
        return reviewer.Approved ? $"{ApprovedMarker} {name}{by}" : name;
    }

    /// <summary>Whole calendar years, months, and days from ready for review to now, and the business days between, counted in UTC.</summary>
    internal readonly record struct Age(int Years, int Months, int Days, int TotalDays, int BusinessDays)
    {
        public bool IsOverdue => BusinessDays > OverdueAfterBusinessDays;

        /// <summary>
        /// Nonzero parts with capital <c>Y</c> and <c>M</c> so months never read as minutes, such as <c>1Y, 2M, 3d</c>, <c>7M</c>, <c>6M, 30d</c>, or <c>3d</c>;
        /// empty when ready the same day.
        /// </summary>
        public override string ToString()
        {
            var parts = new[] { (Years, "Y"), (Months, "M"), (Days, "d") }.Where(p => p.Item1 > 0).Select(p => string.Create(CultureInfo.InvariantCulture, $"{p.Item1}{p.Item2}"));
            return string.Join(AgeSeparator, parts);
        }
    }

    internal static Age AgeOf(DateTimeOffset since, DateTimeOffset now)
    {
        var from = DateOnly.FromDateTime(since.UtcDateTime);
        var to = DateOnly.FromDateTime(now.UtcDateTime);
        if (to < from) to = from;

        const int MonthsPerYear = 12;
        var months = ((to.Year - from.Year) * MonthsPerYear) + to.Month - from.Month;
        if (from.AddMonths(months) > to) months--;
        var days = to.DayNumber - from.AddMonths(months).DayNumber;
        return new Age(months / MonthsPerYear, months % MonthsPerYear, days, to.DayNumber - from.DayNumber, BusinessDaysAfter(from, to));
    }

    /// <summary>Weekdays after <paramref name="from"/> up to and including <paramref name="to"/>, so Friday to Monday is one.</summary>
    private static int BusinessDaysAfter(DateOnly from, DateOnly to)
    {
        const int DaysPerWeek = 7;
        const int WeekdaysPerWeek = 5;
        var total = to.DayNumber - from.DayNumber;
        var count = total / DaysPerWeek * WeekdaysPerWeek;
        for (var day = from.AddDays(total / DaysPerWeek * DaysPerWeek + 1); day <= to; day = day.AddDays(1))
        {
            if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) count++;
        }

        return count;
    }

    /// <summary>Has at least one required reviewer and every one approved; with none, the approvals still needed are unknown.</summary>
    internal static bool IsApproved(AzDoPullRequest pr) => pr.RequiredReviewers.Count > 0 && pr.RequiredReviewers.All(r => r.Approved);

    /// <summary>
    /// What the pull request is waiting for: <see cref="CompletionMarker"/> when approved; otherwise <see cref="OverdueMarker"/> when overdue,
    /// <see cref="NeedsApprovalMarker"/> when not, and nothing for the one being announced, which <see cref="AnnouncedMarker"/> already marks.
    /// </summary>
    /// <param name="age">Null when not measured, as for the one being announced.</param>
    internal static string? StatusMarker(AzDoPullRequest pr, bool isAnnounced, Age? age) =>
        IsApproved(pr) ? CompletionMarker
        : isAnnounced ? null
        : age is { IsOverdue: true } ? OverdueMarker
        : NeedsApprovalMarker;

    private static bool IsAnnounced(AzDoPullRequest pr, string? reference) => PullRequestCardFormatter.Reference(pr.Id) == reference;

    /// <summary>
    /// One pull request, prefixed by <see cref="AnnouncedMarker"/> when it is the one being announced and its <see cref="StatusMarker"/>: its number and linked
    /// title across the full width; under it, author, the date it became ready for review (each viewer's own time zone) and age, repository, and branches,
    /// then its comment count and merge conflicts, and required reviewers, when it has any, with a <see cref="ReviewButtonName"/> button to their right.
    /// </summary>
    private static AdaptiveContainer Block(AzDoPullRequest pr, AzDoCommentCount? comments, DateTimeOffset? readySince, bool isAnnounced, DateTimeOffset now)
    {
        Age? age = !isAnnounced && readySince is { } ready ? AgeOf(ready, now) : null;
        var markers = string.Join(Space, new[] { isAnnounced ? AnnouncedMarker : null, StatusMarker(pr, isAnnounced, age) }.Where(m => m is not null));
        var title = new AdaptiveTextBlock($"{markers} **{PullRequestCardFormatter.Reference(pr.Id)}** [{LinkText(pr.Title)}]({pr.WebUrl.AbsoluteUri})");
        List<AdaptiveElement> details = [FactsLine(pr, readySince, isAnnounced, age)];
        if (CommentsLine(comments, pr.HasConflicts) is { } commentLine) details.Add(commentLine);
        if (pr.RequiredReviewers.Count > 0)
        {
            details.Add(new AdaptiveTextBlock(RequiredReviewersPrefix + string.Join(NameSeparator, pr.RequiredReviewers.Select(Reviewer))) { Spacing = "None" });
        }

        var review = new AdaptiveColumn([new AdaptiveActionSet([new AdaptiveOpenUrlAction(ReviewButtonName, pr.WebUrl.AbsoluteUri)])], AdaptiveColumn.Auto)
        {
            VerticalContentAlignment = "Top",
        };
        return new AdaptiveContainer([title, new AdaptiveColumnSet([new AdaptiveColumn(details, AdaptiveColumn.Stretch), review]) { Spacing = "None" }])
        {
            Separator = true,
        };
    }

    /// <summary>
    /// One wrapping line, such as <c>👤 Josh Jung · 📅 Fri, Apr 24, 2026 (7M) 🌿 repo · source → target</c>, with the age in red when overdue.
    /// The pull request being announced shows <c>📅 Now</c> with no age; one ready since earlier the same day shows its date with no age.
    /// </summary>
    /// <param name="age">From <see cref="Block"/>; null for the one being announced.</param>
    private static AdaptiveRichTextBlock FactsLine(AzDoPullRequest pr, DateTimeOffset? readySince, bool isAnnounced, Age? age)
    {
        var branches = string.Join(BranchArrow, new[] { pr.SourceRefName, pr.TargetRefName }.Where(b => b is not null).Select(b => CardStyle.Branch(b!)));
        var when = isAnnounced ? Now : readySince is { } date ? CardStyle.LocalDate(date) : null;
        var who = Join(pr.Author is { } author ? $"{AuthorMarker} {author}" : null, when is null ? null : $"{ReadyMarker} {when}");

        List<AdaptiveTextRun> runs = [new(who + Space) { IsSubtle = true }];
        if (age is { TotalDays: > 0 } shownAge)
        {
            var ageText = $"({shownAge})";
            runs.Add(shownAge.IsOverdue ? new(ageText) { Color = AlertColor } : new(ageText) { IsSubtle = true });
            runs.Add(new(Space) { IsSubtle = true });
        }

        runs.Add(new(Join($"{RepositoryMarker} {pr.Repository}", branches)) { IsSubtle = true });
        return new AdaptiveRichTextBlock(runs) { Spacing = "None" };
    }

    private static string Join(params string?[] parts) => string.Join(ItemSeparator, parts.Where(p => !string.IsNullOrWhiteSpace(p)));

    /// <summary>Square brackets would end the link text early.</summary>
    private static string LinkText(string title) => title.Replace('[', '(').Replace(']', ')');
}

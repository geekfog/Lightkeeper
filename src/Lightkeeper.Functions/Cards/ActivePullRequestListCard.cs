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

    /// <summary>Longer titles end in an ellipsis, so a title fits one line beside its markers in a typical Teams width and bounds the card's size.</summary>
    public const int MaxTitleChars = 70;

    private const string ItemSeparator = " · ";
    private const string Space = " ";
    private const string AgeSeparator = ", ";
    private const string BranchArrow = " → ";
    private const string NameSeparator = ", ";
    private const string OpenParenthesis = "(";
    private const string CloseParenthesis = ")";
    private const char GroupScopeSeparator = '\\';

    /// <summary>Unresolved comments, merge conflicts, and an overdue age.</summary>
    private static readonly string AlertColor = CardStyle.Failure.TextColor;

    /// <summary>Adaptive Card colors matching the <see cref="ReadyMarker"/> and <see cref="RepositoryMarker"/> emoji.</summary>
    private static readonly string ReadyColor = CardStyle.Review.TextColor;
    private static readonly string RepositoryColor = CardStyle.Success.TextColor;

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
        var subtitle = $"{PullRequestCardFormatter.ReadyForReview}: {reference} {(announced is null ? null : ShownTitle(announced))}".TrimEnd();

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
        var comments = count is { Total: > 0 }
            ? Value(string.Create(CultureInfo.InvariantCulture, $"{CommentsMarker} {count.Resolved}/{count.Total}{CommentsSuffix}"),
                count.Resolved == count.Total ? CardStyle.Success.TextColor : AlertColor)
            : null;
        List<AdaptiveTextRun> runs = [.. Interleave(ItemSeparator, comments, hasConflicts ? Value(ConflictsText, AlertColor) : null)];
        return runs.Count == 0 ? null : Line(runs);
    }

    /// <summary>
    /// Such as <c>✅ Code Reviewers (Jamal Hartnett)</c> for a group a member approved for, or <c>Jamal Hartnett</c> for a user yet to approve;
    /// the names in the default color and the parentheses and commas subtle.
    /// </summary>
    internal static IReadOnlyList<AdaptiveTextRun> Reviewer(AzDoReviewer reviewer)
    {
        var name = reviewer.IsGroup ? reviewer.Name[(reviewer.Name.LastIndexOf(GroupScopeSeparator) + 1)..] : reviewer.Name;
        if (!reviewer.Approved) return [Value(name)];

        var approved = Value($"{ApprovedMarker} {name}");
        if (reviewer.ApprovedBy.Count == 0) return [approved];
        return [approved, Punctuation(Space + OpenParenthesis), .. Interleave(NameSeparator, reviewer.ApprovedBy.Select(by => Value(by))), Punctuation(CloseParenthesis)];
    }

    /// <summary><see cref="RequiredReviewersPrefix"/>, then each <see cref="Reviewer"/> separated by commas, with the label and commas subtle.</summary>
    internal static AdaptiveRichTextBlock RequiredReviewersLine(IReadOnlyList<AzDoReviewer> reviewers) =>
        Line([Punctuation(RequiredReviewersPrefix), .. reviewers.SelectMany((r, i) => i == 0 ? Reviewer(r) : [Punctuation(NameSeparator), .. Reviewer(r)])]);

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
        var title = new AdaptiveTextBlock($"{markers} **{PullRequestCardFormatter.Reference(pr.Id)}** [{LinkText(ShownTitle(pr))}]({pr.WebUrl.AbsoluteUri})");
        List<AdaptiveElement> details = [FactsLine(pr, readySince, isAnnounced, age)];
        if (CommentsLine(comments, pr.HasConflicts) is { } commentLine) details.Add(commentLine);
        if (pr.RequiredReviewers.Count > 0) details.Add(RequiredReviewersLine(pr.RequiredReviewers));

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
    /// One wrapping line, such as <c>🧑 Josh Jung · 📅 Fri, Apr 24, 2026 (7M) 🌿 repo · source → target</c>: the author in <see cref="CardStyle.PersonColor"/>,
    /// the date and age in <see cref="ReadyColor"/> (the age in red when overdue), the repository and branches in <see cref="RepositoryColor"/>, and the dots,
    /// parentheses, and arrow subtle.
    /// The pull request being announced shows <c>📅 Now</c> with no age; one ready since earlier the same day shows its date with no age.
    /// </summary>
    /// <param name="age">From <see cref="Block"/>; null for the one being announced.</param>
    private static AdaptiveRichTextBlock FactsLine(AzDoPullRequest pr, DateTimeOffset? readySince, bool isAnnounced, Age? age)
    {
        var when = isAnnounced ? Now : readySince is { } date ? CardStyle.LocalDate(date) : null;
        List<AdaptiveTextRun> runs =
        [
            .. Interleave(ItemSeparator, pr.Author is { } author ? Value($"{CardStyle.PersonMarker} {author}", CardStyle.PersonColor) : null, when is null ? null : Value($"{ReadyMarker} {when}", ReadyColor)),
            Punctuation(Space),
        ];
        if (age is { TotalDays: > 0 } shownAge)
        {
            runs.AddRange([Punctuation(OpenParenthesis), Value(shownAge.ToString(), shownAge.IsOverdue ? AlertColor : ReadyColor), Punctuation(CloseParenthesis + Space)]);
        }

        var branches = Interleave(BranchArrow, new[] { pr.SourceRefName, pr.TargetRefName }.OfType<string>().Select(b => Value(CardStyle.Branch(b), RepositoryColor))).ToList();
        runs.Add(Value($"{RepositoryMarker} {pr.Repository}", RepositoryColor));
        if (branches.Count > 0) runs.AddRange([Punctuation(ItemSeparator), .. branches]);
        return Line(runs);
    }

    /// <summary>A value in <paramref name="color"/>, or the default color when null.</summary>
    private static AdaptiveTextRun Value(string text, string? color = null) => new(text) { Color = color };

    /// <summary>Labels, separators, and parentheses, subtle so the values stand out.</summary>
    private static AdaptiveTextRun Punctuation(string text) => new(text) { IsSubtle = true };

    /// <summary>The values that are present, with a subtle <paramref name="separator"/> between each.</summary>
    private static IEnumerable<AdaptiveTextRun> Interleave(string separator, params IEnumerable<AdaptiveTextRun?> values) =>
        values.OfType<AdaptiveTextRun>().SelectMany((value, i) => i == 0 ? [value] : new[] { Punctuation(separator), value });

    /// <summary>One detail line under the title; neighboring runs formatted alike are merged, since each run adds to the card's size.</summary>
    private static AdaptiveRichTextBlock Line(IEnumerable<AdaptiveTextRun> runs)
    {
        List<AdaptiveTextRun> merged = [];
        foreach (var run in runs)
        {
            if (merged is [.., var last] && last with { Text = run.Text } == run) merged[^1] = last with { Text = last.Text + run.Text };
            else merged.Add(run);
        }

        return new AdaptiveRichTextBlock(merged) { Spacing = "None" };
    }

    /// <summary>The title cut to <see cref="MaxTitleChars"/>, wherever the card shows it.</summary>
    internal static string ShownTitle(AzDoPullRequest pr) => CardStyle.Truncate(pr.Title, MaxTitleChars);

    /// <summary>Square brackets would end the link text early.</summary>
    private static string LinkText(string title) => title.Replace('[', '(').Replace(']', ')');
}

using System.Globalization;
using System.Text.Json;
using Lightkeeper.Functions.Models;
using Lightkeeper.Functions.Routing;

namespace Lightkeeper.Functions.Cards;

/// <summary>
/// Announces a pull request once, the first time it arrives active and not a draft, whether it was created ready or published later.
/// Register one instance per <c>git.pullrequest.*</c> event type. For rules, <c>Pipeline</c> is the repository and <c>Folder</c> the target branch.
/// </summary>
public sealed class PullRequestCardFormatter(string eventType) : IServiceHookCardFormatter
{
    public static class FactNames
    {
        public const string Project = "📁 Project";
        public const string PullRequest = "🔢 Pull request";
        public const string Author = $"{CardStyle.PersonMarker} Author";
        public const string Reviewers = "👥 Reviewers";
        public const string Created = "🗓️ Created";
    }

    public const string ReadyForReview = "Ready for review";
    public const string ReviewLinkName = "Review pull request";
    public const string DraftSkipReason = "draft";
    public const string RequiredReviewerSuffix = " (required)";

    private const int MaxDescriptionChars = 600;
    private const string ReferencePrefix = "!";
    private const string SubtitleSeparator = " · ";
    private const string BranchArrow = " → ";
    private const string ReviewerSeparator = ", ";
    private const string WebPullRequestSegment = "/pullrequest/";
    private const string AnnounceOnceKeyPrefix = "pullrequest-";

    public string EventType { get; } = eventType;

    public CardFormatResult Format(ServiceHookEvent serviceHookEvent)
    {
        if (serviceHookEvent.Resource.ValueKind != JsonValueKind.Object)
        {
            return CardFormatResult.Invalid(["resource must be a JSON object."]);
        }

        var pr = serviceHookEvent.Resource.Deserialize<PullRequestResource>(LightkeeperJson.Options)!;
        var repository = pr.Repository?.Name;

        List<string> errors = [];
        if (pr.PullRequestId is null) errors.Add("resource.pullRequestId is required.");
        if (string.IsNullOrWhiteSpace(pr.Title)) errors.Add("resource.title is required.");
        if (string.IsNullOrWhiteSpace(pr.Status)) errors.Add("resource.status is required.");
        if (string.IsNullOrWhiteSpace(repository)) errors.Add("resource.repository.name is required.");
        if (errors.Count > 0)
        {
            return CardFormatResult.Invalid(errors);
        }

        var id = pr.PullRequestId!.Value.ToString(CultureInfo.InvariantCulture);
        var source = pr.SourceRefName is { } sourceRef ? CardStyle.Branch(sourceRef) : null;
        var target = pr.TargetRefName is { } targetRef ? CardStyle.Branch(targetRef) : null;
        var isDraft = pr.IsDraft == true;
        var status = isDraft ? PullRequestStatuses.Draft : PullRequestStatuses.DisplayName(pr.Status!);
        var branches = source is null || target is null ? source ?? target : source + BranchArrow + target;
        var facts = new EventFacts(
            pr.Repository?.Project?.Name,
            RulePattern.NormalizeFolder(target),
            repository!,
            status,
            pr.Status!,
            Reference(pr.PullRequestId.Value),
            string.Join(SubtitleSeparator, new[] { repository, branches }.Where(p => p is not null)));

        if (isDraft || !pr.Status!.Equals(PullRequestStatuses.Active, StringComparison.OrdinalIgnoreCase))
        {
            return CardFormatResult.Skip(facts, isDraft ? DraftSkipReason : status.ToLowerInvariant());
        }

        var card = CardStyle.Card(
            CardStyle.Review,
            pr.Title!,
            string.Join(SubtitleSeparator, new[] { ReadyForReview, repository, branches }.Where(p => p is not null)),
            [
                (FactNames.Project, facts.Project),
                (FactNames.PullRequest, facts.Reference),
                (FactNames.Author, pr.CreatedBy?.DisplayName),
                (FactNames.Reviewers, Reviewers(pr.Reviewers)),
            ],
            [(ReviewLinkName, CardStyle.HttpsLink(pr.Links?.Web?.Href, WebLink(pr.Repository?.RemoteUrl, id)))],
            CardStyle.MarkdownExcerpt(pr.Description, MaxDescriptionChars),
            (FactNames.Created, pr.CreationDate));

        return CardFormatResult.Success(card, facts, AnnounceOnceKey(pr.Repository?.Id ?? repository, pr.PullRequestId!.Value));
    }

    /// <summary>The <see cref="Integrations.TableStorage.NotificationTables.Announcement"/> RowKey of a pull request, such as <c>pullrequest-&lt;repository ID&gt;-42</c>.</summary>
    public static string AnnounceOnceKey(string? repository, int id) => string.Create(CultureInfo.InvariantCulture, $"{AnnounceOnceKeyPrefix}{repository}-{id}");

    /// <summary>How cards and rules name a pull request, such as <c>!42</c>.</summary>
    public static string Reference(int pullRequestId) => ReferencePrefix + pullRequestId.ToString(CultureInfo.InvariantCulture);

    /// <summary>Required reviewers first, each marked; null when there are none.</summary>
    internal static string? Reviewers(IReadOnlyList<PullRequestReviewer>? reviewers)
    {
        var names = (reviewers ?? [])
            .Where(r => !string.IsNullOrWhiteSpace(r.DisplayName))
            .OrderByDescending(r => r.IsRequired == true)
            .Select(r => r.DisplayName + (r.IsRequired == true ? RequiredReviewerSuffix : string.Empty))
            .ToList();
        return names.Count == 0 ? null : string.Join(ReviewerSeparator, names);
    }

    /// <summary>The pull request page under the repository's web address, used when the event has no web link.</summary>
    internal static string? WebLink(string? remoteUrl, string id) =>
        string.IsNullOrWhiteSpace(remoteUrl) ? null : remoteUrl.TrimEnd('/') + WebPullRequestSegment + id;
}

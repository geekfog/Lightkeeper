namespace Lightkeeper.Client.AzDo;

/// <summary>Azure DevOps REST calls Lightkeeper makes with the personal access token in <see cref="AzDoOptions"/>.</summary>
public interface IAzDoClient
{
    /// <summary>Every active pull request in the project, drafts included, oldest first.</summary>
    Task<AzDoResult<IReadOnlyList<AzDoPullRequest>>> GetActivePullRequestsAsync(string organization, string project, CancellationToken cancellationToken);

    /// <summary>How many of a pull request's comment threads are resolved, out of all of them; system messages such as pushes and votes are not counted.</summary>
    Task<AzDoResult<AzDoCommentCount>> GetCommentCountAsync(string organization, string project, AzDoPullRequest pullRequest, CancellationToken cancellationToken);
}

public enum AzDoResultStatus
{
    Succeeded,

    /// <summary>No personal access token is set, so no call was made.</summary>
    NotConfigured,

    /// <summary>Azure DevOps answered with anything other than HTTP 200 and readable JSON, such as a sign-in page for a rejected token.</summary>
    Rejected,
    TimedOut,
    Unreachable,
}

/// <param name="StatusCode">HTTP status Azure DevOps returned, when it answered.</param>
public sealed record AzDoResult<T>(AzDoResultStatus Status, T? Value = default, int? StatusCode = null)
{
    public bool Succeeded => Status == AzDoResultStatus.Succeeded;
}

/// <summary>An active pull request as listed for a project.</summary>
/// <param name="RepositoryId">The repository's ID, which the comment thread API needs.</param>
/// <param name="SourceRefName">Full ref name, such as <c>refs/heads/feature/x</c>.</param>
/// <param name="RequiredReviewers">Reviewers that must approve, in the order Azure DevOps lists them.</param>
/// <param name="HasConflicts">The last merge attempt found conflicts with the target branch.</param>
public sealed record AzDoPullRequest(
    int Id,
    string Title,
    bool IsDraft,
    string Repository,
    string? RepositoryId,
    string? SourceRefName,
    string? TargetRefName,
    string? Author,
    DateTimeOffset? Created,
    Uri WebUrl,
    IReadOnlyList<AzDoReviewer> RequiredReviewers,
    bool HasConflicts = false);

/// <param name="Name">A user or a group, such as <c>[Project]\Code Reviewers</c>.</param>
/// <param name="Approved">Voted Approved or Approved with suggestions, directly or through a member voting for the group.</param>
/// <param name="ApprovedBy">For a group, the members who approved on its behalf; empty otherwise.</param>
public sealed record AzDoReviewer(string Name, bool IsGroup, bool Approved, IReadOnlyList<string> ApprovedBy);

public sealed record AzDoCommentCount(int Resolved, int Total);

using Lightkeeper.Client.AzDo;

namespace Lightkeeper.Functions.Tests.TestSupport;

/// <summary>Answers every listing with <see cref="Result"/> and every comment count with <see cref="CommentResult"/>, recording what was asked for.</summary>
internal sealed class FakeAzDoClient : IAzDoClient
{
    public AzDoResult<IReadOnlyList<AzDoPullRequest>> Result { get; set; } = new(AzDoResultStatus.Succeeded, []);

    public AzDoResult<AzDoCommentCount> CommentResult { get; set; } = new(AzDoResultStatus.Succeeded, new AzDoCommentCount(1, 2));

    public List<(string Organization, string Project)> Requests { get; } = [];

    public List<int> CommentRequests { get; } = [];

    public static AzDoPullRequest PullRequest(int id, string title, bool isDraft = false, params AzDoReviewer[] requiredReviewers) =>
        new(id, title, isDraft, "Fabrikam", "repo-id", "refs/heads/feature", "refs/heads/main", "Jamal Hartnett", null,
            new Uri($"https://dev.azure.com/fabrikam/Fabrikam/_git/Fabrikam/pullrequest/{id}"), requiredReviewers);

    public Task<AzDoResult<IReadOnlyList<AzDoPullRequest>>> GetActivePullRequestsAsync(string organization, string project, CancellationToken cancellationToken)
    {
        lock (Requests) Requests.Add((organization, project));
        return Task.FromResult(Result);
    }

    public Task<AzDoResult<AzDoCommentCount>> GetCommentCountAsync(string organization, string project, AzDoPullRequest pullRequest, CancellationToken cancellationToken)
    {
        lock (CommentRequests) CommentRequests.Add(pullRequest.Id);
        return Task.FromResult(CommentResult);
    }
}

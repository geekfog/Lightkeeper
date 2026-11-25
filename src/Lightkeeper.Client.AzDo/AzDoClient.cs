using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lightkeeper.Client.AzDo;

/// <summary>Calls the Azure DevOps REST API with basic authentication and a personal access token. Never logs the token.</summary>
public sealed partial class AzDoClient(HttpClient httpClient, IOptions<AzDoOptions> options, ILogger<AzDoClient> logger) : IAzDoClient
{
    /// <summary>Pull requests requested per page.</summary>
    internal const int PageSize = 100;

    /// <summary>Paging stops here, so a very long listing cannot hold up a notification.</summary>
    internal const int MaxPullRequests = 1000;

    /// <summary>Approved with suggestions is 5 and Approved is 10; waiting for author is -5 and rejected is -10.</summary>
    internal const int MinimumApprovingVote = 5;

    /// <summary>Thread statuses that mean the discussion is resolved; <c>active</c> and <c>pending</c> are not.</summary>
    private static readonly HashSet<string> ResolvedThreadStatuses = new(["fixed", "wontFix", "closed", "byDesign"], StringComparer.OrdinalIgnoreCase);

    private const string SystemCommentType = "system";
    private const string BasicScheme = "Basic";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<AzDoResult<IReadOnlyList<AzDoPullRequest>>> GetActivePullRequestsAsync(string organization, string project, CancellationToken cancellationToken)
    {
        var request = $"pull request listing for {organization}/{project}";
        List<AzDoPullRequest> pullRequests = [];
        for (var skip = 0; skip < MaxPullRequests; skip += PageSize)
        {
            var page = await GetJsonAsync<ListResponse<GitPullRequest>>(AzDoUrls.ActivePullRequests(organization, project, skip, PageSize), request, cancellationToken);
            if (!page.Succeeded) return new(page.Status, StatusCode: page.StatusCode);

            var values = page.Value?.Value ?? [];
            pullRequests.AddRange(values.Select(pr => Map(organization, project, pr)));
            if (values.Count < PageSize) break;
        }

        return new(AzDoResultStatus.Succeeded, [.. pullRequests.OrderBy(pr => pr.Created ?? DateTimeOffset.MaxValue).ThenBy(pr => pr.Id)]);
    }

    public async Task<AzDoResult<AzDoCommentCount>> GetCommentCountAsync(string organization, string project, AzDoPullRequest pullRequest, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(pullRequest.RepositoryId)) return new(AzDoResultStatus.Rejected);

        var threads = await GetJsonAsync<ListResponse<GitThread>>(
            AzDoUrls.PullRequestThreads(organization, project, pullRequest.RepositoryId, pullRequest.Id),
            $"comment threads of pull request {pullRequest.Id} in {organization}/{project}",
            cancellationToken);
        if (!threads.Succeeded) return new(threads.Status, StatusCode: threads.StatusCode);

        var discussions = (threads.Value?.Value ?? [])
            .Where(t => t.IsDeleted != true && (t.Comments ?? []).Any(c => c.IsDeleted != true && !SystemCommentType.Equals(c.CommentType, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        return new(AzDoResultStatus.Succeeded, new AzDoCommentCount(discussions.Count(t => t.Status is { } status && ResolvedThreadStatuses.Contains(status)), discussions.Count));
    }

    /// <summary>One authenticated GET. Failures are logged and returned as a status rather than thrown.</summary>
    /// <param name="request">Names the request in log messages.</param>
    private async Task<AzDoResult<T>> GetJsonAsync<T>(Uri uri, string request, CancellationToken cancellationToken)
    {
        if (!options.Value.IsConfigured)
        {
            LogNotConfigured(logger, AzDoOptions.SectionName, nameof(AzDoOptions.PersonalAccessToken));
            return new(AzDoResultStatus.NotConfigured);
        }

        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Get, uri);
            message.Headers.Authorization = new AuthenticationHeaderValue(BasicScheme, Convert.ToBase64String(Encoding.ASCII.GetBytes(":" + options.Value.PersonalAccessToken)));
            using var response = await httpClient.SendAsync(message, cancellationToken);

            // A rejected token can come back as HTTP 203 with an HTML sign-in page, so only 200 with JSON counts.
            if (response.StatusCode != HttpStatusCode.OK || response.Content.Headers.ContentType?.MediaType != MediaTypeNames.Application.Json)
            {
                LogRejected(logger, request, (int)response.StatusCode);
                return new(AzDoResultStatus.Rejected, StatusCode: (int)response.StatusCode);
            }

            return new(AzDoResultStatus.Succeeded, await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken));
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            LogTimedOut(logger, request, httpClient.Timeout);
            return new(AzDoResultStatus.TimedOut);
        }
        catch (HttpRequestException ex)
        {
            LogUnreachable(logger, request, ex.Message);
            return new(AzDoResultStatus.Unreachable);
        }
        catch (JsonException ex)
        {
            LogUnreadable(logger, request, ex.Message);
            return new(AzDoResultStatus.Rejected, StatusCode: (int)HttpStatusCode.OK);
        }
    }

    private static AzDoPullRequest Map(string organization, string project, GitPullRequest pr)
    {
        var repository = pr.Repository?.Name ?? string.Empty;
        var reviewers = pr.Reviewers ?? [];
        return new AzDoPullRequest(
            pr.PullRequestId,
            pr.Title ?? string.Empty,
            pr.IsDraft == true,
            repository,
            pr.Repository?.Id,
            pr.SourceRefName,
            pr.TargetRefName,
            pr.CreatedBy?.DisplayName,
            pr.CreationDate,
            AzDoUrls.PullRequestPage(organization, project, repository, pr.PullRequestId),
            [.. reviewers.Where(r => r.IsRequired == true && !string.IsNullOrWhiteSpace(r.DisplayName)).Select(r => Reviewer(r, reviewers))]);
    }

    /// <summary>A member's approval of a group shows on the member, whose <c>votedFor</c> names the group.</summary>
    private static AzDoReviewer Reviewer(GitReviewer reviewer, IReadOnlyList<GitReviewer> all)
    {
        string[] approvedBy = reviewer.IsContainer == true
            ? [.. all.Where(m => m.Vote >= MinimumApprovingVote && (m.VotedFor ?? []).Any(g => g.Id == reviewer.Id) && !string.IsNullOrWhiteSpace(m.DisplayName)).Select(m => m.DisplayName!)]
            : [];
        return new AzDoReviewer(reviewer.DisplayName!, reviewer.IsContainer == true, reviewer.Vote >= MinimumApprovingVote || approvedBy.Length > 0, approvedBy);
    }

    /// <summary>Git REST API list responses; only fields Lightkeeper uses are mapped.</summary>
    private sealed record ListResponse<T>(IReadOnlyList<T>? Value);

    private sealed record GitPullRequest(
        int PullRequestId,
        string? Title,
        bool? IsDraft,
        string? SourceRefName,
        string? TargetRefName,
        IdentityRef? CreatedBy,
        DateTimeOffset? CreationDate,
        GitRepositoryRef? Repository,
        IReadOnlyList<GitReviewer>? Reviewers);

    private sealed record IdentityRef(string? Id, string? DisplayName);

    private sealed record GitReviewer(string? Id, string? DisplayName, int? Vote, bool? IsRequired, bool? IsContainer, IReadOnlyList<IdentityRef>? VotedFor);

    private sealed record GitRepositoryRef(string? Id, string? Name);

    private sealed record GitThread(string? Status, bool? IsDeleted, IReadOnlyList<GitComment>? Comments);

    private sealed record GitComment(string? CommentType, bool? IsDeleted);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Azure DevOps was not called: {Section}:{Setting} is empty.")]
    private static partial void LogNotConfigured(ILogger logger, string section, string setting);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Azure DevOps rejected the {Request} with HTTP {StatusCode}.")]
    private static partial void LogRejected(ILogger logger, string request, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Azure DevOps did not answer the {Request} within {Timeout}.")]
    private static partial void LogTimedOut(ILogger logger, string request, TimeSpan timeout);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Azure DevOps could not be reached for the {Request}: {Reason}")]
    private static partial void LogUnreachable(ILogger logger, string request, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Azure DevOps returned an unreadable {Request}: {Reason}")]
    private static partial void LogUnreadable(ILogger logger, string request, string reason);
}

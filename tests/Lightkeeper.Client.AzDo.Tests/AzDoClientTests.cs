using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Lightkeeper.Functions.Tests.TestSupport;

namespace Lightkeeper.Client.AzDo.Tests;

public sealed class AzDoClientTests
{
    private const string Token = "pat-secret";
    private const string Organization = "fabrikam";
    private const string Project = "Fabrikam Fiber";

    [Fact]
    public async Task Lists_active_pull_requests_with_basic_token_auth_oldest_first()
    {
        var handler = new StubHttpMessageHandler(_ => Respond(Page(2, 1)));

        var result = await Client(handler).GetActivePullRequestsAsync(Organization, Project, TestContext.Current.CancellationToken);

        Assert.Equal(AzDoResultStatus.Succeeded, result.Status);
        Assert.Equal([1, 2], result.Value!.Select(pr => pr.Id));
        Assert.Equal(
            "https://dev.azure.com/fabrikam/Fabrikam%20Fiber/_apis/git/pullrequests?searchCriteria.status=active&$top=100&$skip=0&api-version=7.1",
            handler.LastRequest!.RequestUri!.AbsoluteUri);
        Assert.Equal("Basic", handler.LastRequest.Headers.Authorization!.Scheme);
        Assert.Equal(":" + Token, Encoding.ASCII.GetString(Convert.FromBase64String(handler.LastRequest.Headers.Authorization.Parameter!)));

        var first = result.Value![0];
        Assert.Equal("PR 1", first.Title);
        Assert.Equal("repo-1", first.Repository);
        Assert.Equal("Author 1", first.Author);
        Assert.True(first.IsDraft);
        Assert.Equal(new Uri("https://dev.azure.com/fabrikam/Fabrikam%20Fiber/_git/repo-1/pullrequest/1"), first.WebUrl);
        Assert.Equal("repo-id-1", first.RepositoryId);
        Assert.Equal([true, false], result.Value!.Select(pr => pr.HasConflicts));
        Assert.Equal(
            [new AzDoReviewer(@"[DC]\Code Reviewers", true, true, ["Ana Diaz"]), new AzDoReviewer("Lee Park", false, false, [])],
            first.RequiredReviewers,
            (a, b) => a.Name == b.Name && a.IsGroup == b.IsGroup && a.Approved == b.Approved && a.ApprovedBy.SequenceEqual(b.ApprovedBy));
    }

    [Fact]
    public async Task Comment_count_is_resolved_of_user_threads_leaving_out_system_and_deleted_ones()
    {
        var threads = new JsonObject
        {
            ["value"] = new JsonArray(
                Thread("active", "text"), Thread("fixed", "text"), Thread("closed", "text"), Thread(null, "system"), Thread("active", "text", deleted: true)),
        }.ToJsonString();
        var handler = new StubHttpMessageHandler(_ => Respond(threads));

        var result = await Client(handler).GetCommentCountAsync(Organization, Project, PullRequest(7), TestContext.Current.CancellationToken);

        Assert.Equal(new AzDoCommentCount(2, 3), result.Value);
        Assert.Equal(
            "https://dev.azure.com/fabrikam/Fabrikam%20Fiber/_apis/git/repositories/repo-id-7/pullRequests/7/threads?api-version=7.1",
            handler.LastRequest!.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task Comment_count_without_repository_id_makes_no_call()
    {
        var handler = new StubHttpMessageHandler(_ => Respond("{}"));

        var result = await Client(handler).GetCommentCountAsync(Organization, Project, PullRequest(7) with { RepositoryId = null }, TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public async Task Full_pages_are_followed_until_a_short_page()
    {
        var calls = 0;
        var handler = new StubHttpMessageHandler(_ => Respond(++calls == 1 ? Page([.. Enumerable.Range(1, AzDoClient.PageSize)]) : Page(AzDoClient.PageSize + 1)));

        var result = await Client(handler).GetActivePullRequestsAsync(Organization, Project, TestContext.Current.CancellationToken);

        Assert.Equal(2, calls);
        Assert.Equal(AzDoClient.PageSize + 1, result.Value!.Count);
        Assert.Contains($"$skip={AzDoClient.PageSize}", handler.LastRequest!.RequestUri!.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_token_makes_no_call()
    {
        var handler = new StubHttpMessageHandler(_ => Respond(Page()));

        var result = await Client(handler, token: " ").GetActivePullRequestsAsync(Organization, Project, TestContext.Current.CancellationToken);

        Assert.Equal(AzDoResultStatus.NotConfigured, result.Status);
        Assert.Null(handler.LastRequest);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "")]
    [InlineData(HttpStatusCode.NonAuthoritativeInformation, "<html>sign in</html>")]
    public async Task Refused_or_sign_in_page_is_rejected(HttpStatusCode status, string body)
    {
        var handler = StubHttpMessageHandler.Returning(status, body);

        var result = await Client(handler).GetActivePullRequestsAsync(Organization, Project, TestContext.Current.CancellationToken);

        Assert.Equal(AzDoResultStatus.Rejected, result.Status);
        Assert.Equal((int)status, result.StatusCode);
    }

    [Fact]
    public async Task No_answer_within_timeout_is_timed_out()
    {
        var result = await Client(StubHttpMessageHandler.NeverResponding(), timeout: TimeSpan.FromMilliseconds(50))
            .GetActivePullRequestsAsync(Organization, Project, TestContext.Current.CancellationToken);

        Assert.Equal(AzDoResultStatus.TimedOut, result.Status);
    }

    [Fact]
    public async Task Connection_failure_is_unreachable()
    {
        var result = await Client(StubHttpMessageHandler.Throwing(new HttpRequestException("no route")))
            .GetActivePullRequestsAsync(Organization, Project, TestContext.Current.CancellationToken);

        Assert.Equal(AzDoResultStatus.Unreachable, result.Status);
    }

    private static AzDoPullRequest PullRequest(int id) =>
        new(id, "PR", false, "repo", $"repo-id-{id}", null, null, null, null, new Uri("https://dev.azure.com/x/y/_git/repo/pullrequest/1"), []);

    private static JsonObject Thread(string? status, string commentType, bool deleted = false) =>
        new JsonObject { ["status"] = status, ["isDeleted"] = deleted, ["comments"] = new JsonArray(new JsonObject { ["commentType"] = commentType }) };

    private static AzDoClient Client(StubHttpMessageHandler handler, string token = Token, TimeSpan? timeout = null) =>
        new(new HttpClient(handler) { Timeout = timeout ?? TimeSpan.FromSeconds(5) },
            Options.Create(new AzDoOptions { PersonalAccessToken = token }),
            NullLogger<AzDoClient>.Instance);

    private static Task<HttpResponseMessage> Respond(string json) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });

    /// <summary>A Git REST API listing; pull request 1 is the oldest and a draft.</summary>
    private static string Page(params int[] ids) =>
        new JsonObject
        {
            ["count"] = ids.Length,
            ["value"] = new JsonArray([.. ids.Select(id => (JsonNode)new JsonObject
            {
                ["pullRequestId"] = id,
                ["title"] = $"PR {id}",
                ["isDraft"] = id == 1,
                ["creationDate"] = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero).AddDays(id).ToString("O"),
                ["createdBy"] = new JsonObject { ["displayName"] = $"Author {id}" },
                ["repository"] = new JsonObject { ["id"] = $"repo-id-{id}", ["name"] = $"repo-{id}" },
                ["reviewers"] = new JsonArray(
                    new JsonObject { ["id"] = "group", ["displayName"] = @"[DC]\Code Reviewers", ["isRequired"] = true, ["isContainer"] = true, ["vote"] = 0 },
                    new JsonObject { ["id"] = "ana", ["displayName"] = "Ana Diaz", ["vote"] = 10, ["votedFor"] = new JsonArray(new JsonObject { ["id"] = "group" }) },
                    new JsonObject { ["id"] = "lee", ["displayName"] = "Lee Park", ["isRequired"] = true, ["vote"] = -5 }),
                ["targetRefName"] = "refs/heads/main",
                ["mergeStatus"] = id == 1 ? "conflicts" : "succeeded",
            })]),
        }.ToJsonString();
}

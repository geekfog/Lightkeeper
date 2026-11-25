using System.Globalization;

namespace Lightkeeper.Client.AzDo;

/// <summary>Azure DevOps Services addresses: REST endpoints and the web pages they correspond to.</summary>
public static class AzDoUrls
{
    public const string ServicesHost = "dev.azure.com";
    public const string ApiVersion = "7.1";

    public static readonly Uri ServicesBaseUrl = new($"https://{ServicesHost}/");

    public static Uri Project(string organization, string project) => new(ServicesBaseUrl, $"{Segment(organization)}/{Segment(project)}/");

    public static Uri PullRequestPage(string organization, string project, string repository, int pullRequestId) =>
        new(Project(organization, project), string.Create(CultureInfo.InvariantCulture, $"_git/{Segment(repository)}/pullrequest/{pullRequestId}"));

    /// <summary>Git REST API "Pull Requests - Get Pull Requests By Project", filtered to active pull requests, one page at a time.</summary>
    internal static Uri ActivePullRequests(string organization, string project, int skip, int top) =>
        new(Project(organization, project),
            string.Create(CultureInfo.InvariantCulture, $"_apis/git/pullrequests?searchCriteria.status=active&$top={top}&$skip={skip}&api-version={ApiVersion}"));

    /// <summary>Git REST API "Pull Request Threads - List".</summary>
    internal static Uri PullRequestThreads(string organization, string project, string repositoryId, int pullRequestId) =>
        new(Project(organization, project),
            string.Create(CultureInfo.InvariantCulture, $"_apis/git/repositories/{Segment(repositoryId)}/pullRequests/{pullRequestId}/threads?api-version={ApiVersion}"));

    private static string Segment(string value) => Uri.EscapeDataString(value.Trim());
}

using System.Text.Json.Serialization;

namespace Lightkeeper.Functions.Models;

/// <summary>The <c>resource</c> of a <c>git.pullrequest.*</c> event (resource version 2.0). Only fields Lightkeeper uses are mapped.</summary>
/// <param name="IsDraft">Absent from Microsoft's samples; missing is treated as published.</param>
public sealed record PullRequestResource(
    int? PullRequestId,
    string? Status,
    bool? IsDraft,
    string? Title,
    string? Description,
    string? SourceRefName,
    string? TargetRefName,
    IdentityReference? CreatedBy,
    DateTimeOffset? CreationDate,
    IReadOnlyList<PullRequestReviewer>? Reviewers,
    GitRepositoryReference? Repository,
    string? Url,
    [property: JsonPropertyName("_links")] BuildLinks? Links);

public sealed record PullRequestReviewer(string? DisplayName, int? Vote, bool? IsRequired);

/// <param name="RemoteUrl">The repository's web address, such as <c>https://dev.azure.com/org/project/_git/repo</c>.</param>
public sealed record GitRepositoryReference(string? Id, string? Name, ProjectReference? Project, string? RemoteUrl);

/// <summary>Values of <see cref="PullRequestResource.Status"/>, from the Azure DevOps Git REST API PullRequestStatus enumeration.</summary>
public static class PullRequestStatuses
{
    public const string Active = "active";
    public const string Abandoned = "abandoned";
    public const string Completed = "completed";

    /// <summary>The status rules match on for a draft, which Azure DevOps reports as active with <see cref="PullRequestResource.IsDraft"/> set.</summary>
    public const string Draft = "Draft";

    private static readonly Dictionary<string, string> DisplayNames = new(StringComparer.OrdinalIgnoreCase)
    {
        [Active] = "Active",
        [Abandoned] = "Abandoned",
        [Completed] = "Completed",
    };

    public static string DisplayName(string status) => DisplayNames.TryGetValue(status, out var name) ? name : status;
}

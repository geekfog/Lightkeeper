using System.Text.Json.Serialization;

namespace Lightkeeper.Functions.Models;

/// <summary>The <c>resource</c> of a <c>build.complete</c> event (resource version 2.0). Only fields Lightkeeper uses are mapped.</summary>
public sealed record BuildResource(
    int? Id,
    string? BuildNumber,
    string? Status,
    string? Result,
    string? Url,
    [property: JsonPropertyName("_links")] BuildLinks? Links,
    BuildDefinitionReference? Definition,
    ProjectReference? Project,
    string? SourceBranch,
    string? Reason,
    IdentityReference? RequestedFor,
    RepositoryReference? Repository,
    DateTimeOffset? StartTime,
    DateTimeOffset? FinishTime);

public sealed record BuildLinks(LinkReference? Web);

public sealed record LinkReference(string? Href);

/// <param name="Path">Folder holding the pipeline, such as <c>\</c> or <c>\prd\infra</c>.</param>
public sealed record BuildDefinitionReference(int? Id, string? Name, string? Path, ProjectReference? Project);

public sealed record ProjectReference(string? Id, string? Name);

public sealed record IdentityReference(string? DisplayName);

public sealed record RepositoryReference(string? Name);

/// <summary>Values of <see cref="BuildResource.Result"/>, from the Azure DevOps Build REST API BuildResult enumeration.</summary>
public static class BuildResults
{
    public const string Succeeded = "succeeded";
    public const string PartiallySucceeded = "partiallySucceeded";
    public const string Failed = "failed";
    public const string Canceled = "canceled";

    private static readonly Dictionary<string, string> DisplayNames = new(StringComparer.OrdinalIgnoreCase)
    {
        [Succeeded] = "Succeeded",
        [PartiallySucceeded] = "Partially Succeeded",
        [Failed] = "Failed",
        [Canceled] = "Stopped",
    };

    /// <summary>The name Azure DevOps shows for a result, as in the service-hook build status filter.</summary>
    public static string DisplayName(string result) => DisplayNames.TryGetValue(result, out var name) ? name : result;
}

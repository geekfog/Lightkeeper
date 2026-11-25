using System.Globalization;
using System.Text.Json;
using Lightkeeper.Functions.Models;
using Lightkeeper.Functions.Routing;

namespace Lightkeeper.Functions.Cards;

/// <summary>Formats <c>build.complete</c> events, based on the sample in the Azure DevOps service-hook event reference.</summary>
public sealed class BuildCompletedCardFormatter : IServiceHookCardFormatter
{
    public static class FactNames
    {
        public const string Project = "📁 Project";
        public const string Branch = "🌿 Branch";
        public const string Repository = "📦 Repository";
        public const string RequestedFor = $"{CardStyle.PersonMarker} Requested for";
        public const string Reason = "⚡ Reason";
        public const string Duration = "⏱️ Duration";
        public const string Finished = "🏁 Finished";
    }

    public static class LinkNames
    {
        public const string Build = "View build";
        public const string Pipeline = "View pipeline";
    }

    private const string ResultsSegment = "/results";
    private const string BuildResultsPathSuffix = "/_build" + ResultsSegment;
    private const string DefinitionIdQuery = "definitionId=";
    private const string SubtitleSeparator = " · ";
    private const int FactColumns = 2;

    public string EventType => ServiceHookEventTypes.BuildCompleted;

    public CardFormatResult Format(ServiceHookEvent serviceHookEvent)
    {
        if (serviceHookEvent.Resource.ValueKind != JsonValueKind.Object)
        {
            return CardFormatResult.Invalid(["resource must be a JSON object."]);
        }

        var build = serviceHookEvent.Resource.Deserialize<BuildResource>(LightkeeperJson.Options)!;
        var pipeline = build.Definition?.Name;

        List<string> errors = [];
        if (build.Id is null) errors.Add("resource.id is required.");
        if (string.IsNullOrWhiteSpace(build.BuildNumber)) errors.Add("resource.buildNumber is required.");
        if (string.IsNullOrWhiteSpace(build.Result)) errors.Add("resource.result is required.");
        if (string.IsNullOrWhiteSpace(pipeline)) errors.Add("resource.definition.name is required.");
        if (errors.Count > 0)
        {
            return CardFormatResult.Invalid(errors);
        }

        var project = build.Project?.Name ?? build.Definition?.Project?.Name;
        var status = BuildResults.DisplayName(build.Result!);
        var buildLink = CardStyle.HttpsLink(build.Links?.Web?.Href, build.Url);

        var card = CardStyle.Card(
            CardStyle.BuildResult(build.Result!),
            $"Build {status.ToLowerInvariant()}",
            pipeline + SubtitleSeparator + build.BuildNumber,
            [
                (FactNames.Project, project),
                (FactNames.Branch, build.SourceBranch is { } branch ? CardStyle.Branch(branch) : null),
                (FactNames.Repository, build.Repository?.Name),
                (FactNames.RequestedFor, build.RequestedFor?.DisplayName),
                (FactNames.Reason, build.Reason is { } reason ? CardStyle.BuildReason(reason) : null),
                (FactNames.Duration, build is { StartTime: { } start, FinishTime: { } end } ? CardStyle.Duration(end - start) : null),
            ],
            [
                (LinkNames.Build, buildLink),
                (LinkNames.Pipeline, PipelineLink(buildLink, build.Definition?.Id)),
            ],
            when: (FactNames.Finished, build.FinishTime),
            factColumns: FactColumns,
            linksInHeader: true);

        var eventFacts = new EventFacts(project, RulePattern.NormalizeFolder(build.Definition?.Path), pipeline!, status, build.Result!, build.BuildNumber);
        return CardFormatResult.Success(card, eventFacts);
    }

    /// <summary>Derives the pipeline page from the build results page (<c>…/_build/results?buildId=n</c> to <c>…/_build?definitionId=n</c>); null for any other link shape.</summary>
    internal static string? PipelineLink(string? buildLink, int? definitionId)
    {
        if (definitionId is null || !Uri.TryCreate(buildLink, UriKind.Absolute, out var uri) || !uri.AbsolutePath.EndsWith(BuildResultsPathSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var buildPath = uri.AbsolutePath[..^ResultsSegment.Length];
        return new UriBuilder(uri) { Path = buildPath, Query = DefinitionIdQuery + definitionId.Value.ToString(CultureInfo.InvariantCulture) }.Uri.AbsoluteUri;
    }
}

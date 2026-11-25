using System.Text.Json.Nodes;
using Lightkeeper.Functions.Cards;
using Lightkeeper.Functions.Models;
using Lightkeeper.Functions.Tests.TestSupport;
using Facts = Lightkeeper.Functions.Cards.BuildCompletedCardFormatter.FactNames;
using Links = Lightkeeper.Functions.Cards.BuildCompletedCardFormatter.LinkNames;

namespace Lightkeeper.Functions.Tests.Cards;

public sealed class BuildCompletedCardFormatterTests
{
    private const string SampleBuildLink = "https://dev.azure.com/FabrikamFiber/e4e4e4e4-ffff-aaaa-bbbb-c5c5c5c5c5c5/_build/results?buildId=2727068";

    private readonly BuildCompletedCardFormatter _formatter = new();

    [Fact]
    public void Official_sample_maps_to_tinted_header_facts_and_buttons()
    {
        var card = _formatter.Format(Samples.ReadEvent(Samples.BuildCompleted)).Card!;

        var (header, headerText, facts, buttons) = Parts(card);
        Assert.Equal(CardStyle.Success.ContainerStyle, header.Style);
        Assert.Equal(
            ["✅ Build succeeded", "MainRepo (1) · 20241202.1", "🏁 Finished {{DATE(2024-12-02T12:21:08Z, SHORT)}} {{TIME(2024-12-02T12:21:08Z)}}"],
            headerText.Select(t => t.Text));
        Assert.Equal("Build succeeded: MainRepo (1) · 20241202.1", card.FallbackText);
        Assert.Equal(
            new Dictionary<string, string>
            {
                [Facts.Project] = "FabrikamFiberChat",
                [Facts.Branch] = "main",
                [Facts.Repository] = "MainRepo",
                [Facts.RequestedFor] = "Fabrikam Fiber",
                [Facts.Reason] = "Manual",
                [Facts.Duration] = "2m 11s",
            },
            facts.ToDictionary(f => f.Title, f => f.Value));
        Assert.Equal([3, 3], Assert.IsType<AdaptiveColumnSet>(card.Body[1]).Columns.Select(c => Assert.IsType<AdaptiveFactSet>(Assert.Single(c.Items)).Facts.Count));
        Assert.Empty(card.Actions);
        Assert.Equal(
            [
                new AdaptiveOpenUrlAction(Links.Build, SampleBuildLink),
                new AdaptiveOpenUrlAction(Links.Pipeline, "https://dev.azure.com/FabrikamFiber/e4e4e4e4-ffff-aaaa-bbbb-c5c5c5c5c5c5/_build?definitionId=4658"),
            ],
            buttons);
    }

    [Fact]
    public void Minimal_sample_omits_missing_facts_and_links_to_resource_url()
    {
        var card = _formatter.Format(Samples.ReadEvent(Samples.BuildCompletedMinimal)).Card!;

        var (_, _, facts, buttons) = Parts(card);
        Assert.Equal([Facts.Project], facts.Select(f => f.Title));
        var action = Assert.Single(buttons);
        Assert.Equal(Links.Build, action.Title);
        Assert.StartsWith("https://dev.azure.com/FabrikamFiber/web/build.aspx", action.Url);
    }

    [Theory]
    [InlineData(BuildResults.PartiallySucceeded, "⚠️ Build partially succeeded", "warning")]
    [InlineData(BuildResults.Failed, "❌ Build failed", "attention")]
    [InlineData(BuildResults.Canceled, "⏹️ Build stopped", "emphasis")]
    [InlineData("somethingNew", "⏹️ Build somethingnew", "emphasis")]
    public void Result_sets_emoji_title_and_header_tint(string result, string title, string style)
    {
        var (header, headerText, _, _) = Parts(_formatter.Format(WithResource(r => r["result"] = result)).Card!);

        Assert.Equal(title, headerText[0].Text);
        Assert.Equal(style, header.Style);
    }

    [Theory]
    [InlineData("id", "resource.id is required.")]
    [InlineData("buildNumber", "resource.buildNumber is required.")]
    [InlineData("result", "resource.result is required.")]
    [InlineData("definition", "resource.definition.name is required.")]
    public void Missing_required_field_is_reported(string field, string error)
    {
        var result = _formatter.Format(WithResource(r => r.Remove(field)));

        Assert.Null(result.Card);
        Assert.Equal([error], result.Errors);
    }

    [Fact]
    public void Non_https_links_are_not_offered()
    {
        var card = _formatter.Format(WithResource(r =>
        {
            r.Remove("_links");
            r["url"] = "http://example.test/build";
        })).Card!;

        Assert.Empty(Parts(card).Buttons);
    }

    [Theory]
    [InlineData(SampleBuildLink, 7, "https://dev.azure.com/FabrikamFiber/e4e4e4e4-ffff-aaaa-bbbb-c5c5c5c5c5c5/_build?definitionId=7")]
    [InlineData("https://dev.azure.com/org/web/build.aspx?builduri=x", 7, null)]
    [InlineData(SampleBuildLink, null, null)]
    [InlineData(null, 7, null)]
    public void Pipeline_link_is_derived_only_from_a_build_results_link(string? buildLink, int? definitionId, string? expected) =>
        Assert.Equal(expected, BuildCompletedCardFormatter.PipelineLink(buildLink, definitionId));

    [Theory]
    [InlineData(9, "9s")]
    [InlineData(78, "1m 18s")]
    [InlineData(3725, "1h 02m 05s")]
    public void Duration_is_compact(int seconds, string expected) => Assert.Equal(expected, CardStyle.Duration(TimeSpan.FromSeconds(seconds)));

    [Theory]
    [InlineData("batchedCI", "Batched CI")]
    [InlineData("somethingNew", "somethingNew")]
    public void Reason_uses_display_name_when_known(string reason, string expected) => Assert.Equal(expected, CardStyle.BuildReason(reason));

    /// <summary>The header, its text, every fact in column order, and the buttons stacked at the header's right, each alone in a right-aligned row.</summary>
    private static (AdaptiveContainer Header, IReadOnlyList<AdaptiveTextBlock> HeaderText, IReadOnlyList<AdaptiveFact> Facts, IReadOnlyList<AdaptiveOpenUrlAction> Buttons) Parts(AdaptiveCard card)
    {
        var header = Assert.IsType<AdaptiveContainer>(card.Body[0]);
        var (headerText, buttons) = header.Items is [AdaptiveColumnSet { Columns: [var text, var side] }]
            ? (text.Items, side.Items.Select(RowButton).ToList())
            : (header.Items, []);
        var facts = card.Body[1] switch
        {
            AdaptiveColumnSet columns => columns.Columns.SelectMany(c => Assert.IsType<AdaptiveFactSet>(Assert.Single(c.Items)).Facts).ToList(),
            var single => Assert.IsType<AdaptiveFactSet>(single).Facts,
        };
        return (header, [.. headerText.Cast<AdaptiveTextBlock>()], facts, buttons);
    }

    private static AdaptiveOpenUrlAction RowButton(AdaptiveElement row)
    {
        var columns = Assert.IsType<AdaptiveColumnSet>(row);
        Assert.Equal("Right", columns.HorizontalAlignment);
        return Assert.Single(Assert.IsType<AdaptiveActionSet>(Assert.Single(Assert.Single(columns.Columns).Items)).Actions);
    }

    private static ServiceHookEvent WithResource(Action<JsonObject> change)
    {
        var node = Samples.ReadNode(Samples.BuildCompleted);
        change(node["resource"]!.AsObject());
        return Samples.Parse(node);
    }
}

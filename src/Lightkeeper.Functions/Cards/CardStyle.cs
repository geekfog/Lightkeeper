using System.Globalization;
using Lightkeeper.Functions.Models;

namespace Lightkeeper.Functions.Cards;

/// <summary>Display rules shared by every card formatter.</summary>
public static class CardStyle
{
    /// <summary>Emoji and Adaptive Card colors for one outcome. <paramref name="ContainerStyle"/> tints the header; <paramref name="TextColor"/> colors its title.</summary>
    /// <param name="BackgroundColor">A <c>#RRGGBB</c> header color for outcomes no style matches; <paramref name="ContainerStyle"/> remains the fallback, and all header text uses <paramref name="TextColor"/>.</param>
    public sealed record Tone(string Emoji, string ContainerStyle, string TextColor, string? BackgroundColor = null);

    public static readonly Tone Success = new("✅", "good", "good");
    public static readonly Tone Warning = new("⚠️", "warning", "warning");
    public static readonly Tone Failure = new("❌", "attention", "attention");
    public static readonly Tone Neutral = new("⏹️", "emphasis", "light", "#4B5563");
    public static readonly Tone Review = new("👀", "accent", "accent");
    public static readonly Tone Info = new("🔔", "emphasis", "light", "#1F5F5B");
    public static readonly Tone WorkItem = new("📋", "emphasis", "light", "#4A2C6D");

    /// <summary>In front of a person's name on every card: the gender-neutral person emoji, whose yellow pairs with <see cref="PersonColor"/>.</summary>
    public const string PersonMarker = "🧑";

    /// <summary>Amber, the Adaptive Card color closest to <see cref="PersonMarker"/>, for a person's name where a card can color it.</summary>
    public static readonly string PersonColor = Warning.TextColor;

    private const string Ellipsis = "…";
    internal const string RightAlignment = "Right";
    private const string ButtonSpacing = "Small";
    private static readonly char[] MarkdownBlockMarkers = ['#', '>'];

    /// <summary>The RFC 3339 form the Adaptive Card <c>DATE</c> and <c>TIME</c> functions require.</summary>
    private const string CardDateTimeFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";
    private const string HeadingMarker = "#";
    private const string BoldMarker = "**";
    private const string CR = "\r";
    private const string LF = "\n";
    private const string BranchPrefix = "refs/heads/";

    private static readonly Dictionary<string, Tone> BuildResultTones = new(StringComparer.OrdinalIgnoreCase)
    {
        [BuildResults.Succeeded] = Success,
        [BuildResults.PartiallySucceeded] = Warning,
        [BuildResults.Failed] = Failure,
        [BuildResults.Canceled] = Neutral,
    };

    /// <summary>Display names for Azure DevOps build reasons (Build REST API BuildReason enumeration); unknown reasons show as sent.</summary>
    private static readonly Dictionary<string, string> BuildReasons = new(StringComparer.OrdinalIgnoreCase)
    {
        ["manual"] = "Manual",
        ["individualCI"] = "CI",
        ["batchedCI"] = "Batched CI",
        ["schedule"] = "Scheduled",
        ["scheduleForced"] = "Scheduled (forced)",
        ["userCreated"] = "User created",
        ["validateShelveset"] = "Validate shelveset",
        ["checkInShelveset"] = "Gated check-in",
        ["pullRequest"] = "Pull request",
        ["buildCompletion"] = "Build completion",
        ["resourceTrigger"] = "Resource trigger",
        ["triggered"] = "Triggered",
    };

    public static Tone BuildResult(string result) => BuildResultTones.TryGetValue(result, out var tone) ? tone : Neutral;

    public static string BuildReason(string reason) => BuildReasons.TryGetValue(reason, out var name) ? name : reason;

    /// <summary>A date and time Teams shows in each viewer's own time zone, such as <c>Sat, Nov 21, 2026 7:44 AM</c>. Works in text blocks.</summary>
    public static string LocalDateTime(DateTimeOffset value) => $"{LocalDate(value)} {{{{TIME({CardUtc(value)})}}}}";

    /// <summary>A date Teams shows in each viewer's own time zone, such as <c>Fri, Apr 24, 2026</c>. Works in text blocks.</summary>
    public static string LocalDate(DateTimeOffset value) => $"{{{{DATE({CardUtc(value)}, SHORT)}}}}";

    private static string CardUtc(DateTimeOffset value) => value.UtcDateTime.ToString(CardDateTimeFormat, CultureInfo.InvariantCulture);

    /// <summary>Compact duration such as <c>1h 02m 05s</c>, <c>1m 18s</c>, or <c>9s</c>.</summary>
    public static string Duration(TimeSpan value) => value switch
    {
        { TotalHours: >= 1 } => string.Create(CultureInfo.InvariantCulture, $"{(int)value.TotalHours}h {value.Minutes:00}m {value.Seconds:00}s"),
        { TotalMinutes: >= 1 } => string.Create(CultureInfo.InvariantCulture, $"{value.Minutes}m {value.Seconds:00}s"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{Math.Max(0, value.Seconds)}s"),
    };

    public static string Branch(string sourceBranch) =>
        sourceBranch.StartsWith(BranchPrefix, StringComparison.Ordinal) ? sourceBranch[BranchPrefix.Length..] : sourceBranch;

    /// <summary>
    /// Plain one-paragraph text cut to <paramref name="maxChars"/>, ending with an ellipsis when cut; null when nothing is left.
    /// Heading and quote markers are dropped from each line so a markdown description cannot render as one giant heading.
    /// </summary>
    public static string? Excerpt(string? text, int maxChars)
    {
        var lines = (text ?? string.Empty).Split('\n').Select(line => line.Trim().TrimStart(MarkdownBlockMarkers).Trim());
        var collapsed = string.Join(' ', string.Join(' ', lines).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Length == 0 ? null : Truncate(collapsed, maxChars);
    }

    /// <summary>The text cut to <paramref name="maxChars"/>, ending with an ellipsis when cut.</summary>
    public static string Truncate(string text, int maxChars) =>
        text.Length <= maxChars ? text : text[..(maxChars - Ellipsis.Length)].TrimEnd() + Ellipsis;

    /// <summary>
    /// Markdown kept with its line breaks, so lists, bold, and links render. Headings become bold lines so they cannot fill the card.
    /// Whole lines are kept up to <paramref name="maxChars"/>, ending with an ellipsis when cut; null when blank.
    /// </summary>
    public static string? MarkdownExcerpt(string? text, int maxChars)
    {
        var lines = (text ?? string.Empty).Replace(CR + LF, LF, StringComparison.Ordinal).Trim().Split(LF).Select(HeadingToBold).ToList();
        if (lines is [""]) return null;

        List<string> kept = [];
        var length = 0;
        foreach (var line in lines)
        {
            if (length + line.Length > maxChars)
            {
                if (kept.Count == 0) kept.Add(line[..(maxChars - Ellipsis.Length)].TrimEnd());
                return string.Join(LF, kept).TrimEnd() + Ellipsis;
            }

            kept.Add(line);
            length += line.Length + LF.Length;
        }

        return string.Join(LF, kept);
    }

    private static string HeadingToBold(string line)
    {
        var trimmed = line.Trim();
        if (!trimmed.StartsWith(HeadingMarker, StringComparison.Ordinal)) return line.TrimEnd();
        var heading = trimmed.TrimStart(HeadingMarker[0]).Trim();
        return heading.Length == 0 ? string.Empty : BoldMarker + heading + BoldMarker;
    }

    /// <summary>Returns the first candidate that is an absolute https URL, so cards never link to anything else.</summary>
    public static string? HttpsLink(params string?[] candidates) =>
        candidates.FirstOrDefault(c => Uri.TryCreate(c, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps);

    /// <summary>
    /// The standard card: a tinted header with the title, subtitle, and optional time in the viewer's own time zone; a fact list; optional markdown details; and link buttons.
    /// Facts and links with no value are left out. <paramref name="factColumns"/> splits the facts into that many side-by-side columns, filled top to bottom.
    /// <paramref name="linksInHeader"/> stacks the link buttons at the right of the header instead of at the foot of the card.
    /// </summary>
    public static AdaptiveCard Card(
        Tone tone,
        string title,
        string subtitle,
        IEnumerable<(string Label, string? Value)> facts,
        IEnumerable<(string Title, string? Url)> links,
        string? details = null,
        (string Label, DateTimeOffset? Value) when = default,
        int factColumns = 1,
        bool linksInHeader = false)
    {
        var buttons = Links(links);
        List<AdaptiveElement> body = [Header(tone, title, subtitle, when, linksInHeader ? buttons : []), Facts(facts, factColumns)];
        if (details is not null) body.Add(new AdaptiveTextBlock(details));

        return new AdaptiveCard
        {
            FallbackText = $"{title}: {subtitle}",
            Body = body,
            Actions = linksInHeader ? [] : buttons,
        };
    }

    /// <summary>One fact set, or a column set of fact sets when <paramref name="columns"/> is more than one and there are enough facts to split.</summary>
    private static AdaptiveElement Facts(IEnumerable<(string Label, string? Value)> facts, int columns)
    {
        AdaptiveFact[] shown = [.. facts.Where(f => !string.IsNullOrWhiteSpace(f.Value)).Select(f => new AdaptiveFact(f.Label, f.Value!))];
        if (columns <= 1 || shown.Length <= 1) return new AdaptiveFactSet(shown);

        var perColumn = (int)Math.Ceiling(shown.Length / (double)columns);
        return new AdaptiveColumnSet([.. shown.Chunk(perColumn).Select(chunk => new AdaptiveColumn([new AdaptiveFactSet(chunk)], AdaptiveColumn.Stretch))]);
    }

    /// <summary>Link buttons; links with no value are left out.</summary>
    public static IReadOnlyList<AdaptiveOpenUrlAction> Links(IEnumerable<(string Title, string? Url)> links) =>
        [.. links.Where(l => l.Url is not null).Select(l => new AdaptiveOpenUrlAction(l.Title, l.Url!))];

    /// <summary>The tinted header with the title, subtitle, and optional time in the viewer's own time zone, and any <paramref name="links"/> stacked at its right.</summary>
    public static AdaptiveContainer Header(
        Tone tone, string title, string subtitle, (string Label, DateTimeOffset? Value) when = default, IReadOnlyList<AdaptiveOpenUrlAction>? links = null)
    {
        // On a custom background the subtitle and time follow the title color, since the theme's default text may not contrast with it.
        var detailColor = tone.BackgroundColor is null ? null : tone.TextColor;
        List<AdaptiveElement> headerItems =
        [
            new AdaptiveTextBlock($"{tone.Emoji} {title}") { Size = "Large", Weight = "Bolder", Color = tone.TextColor },
            new AdaptiveTextBlock(subtitle) { IsSubtle = true, Spacing = "None", Color = detailColor },
        ];
        if (when.Value is { } time)
        {
            headerItems.Add(new AdaptiveTextBlock($"{when.Label} {LocalDateTime(time)}") { IsSubtle = true, Spacing = "None", Color = detailColor });
        }

        IReadOnlyList<AdaptiveElement> items = links is { Count: > 0 }
            ?
            [
                new AdaptiveColumnSet(
                [
                    new AdaptiveColumn(headerItems, AdaptiveColumn.Stretch),
                    new AdaptiveColumn([.. links.Select(RightAlignedButton)], AdaptiveColumn.Auto) { VerticalContentAlignment = "Center" },
                ]),
            ]
            : headerItems;

        return new AdaptiveContainer(items, tone.ContainerStyle)
        {
            Bleed = true,
            BackgroundImage = tone.BackgroundColor is { } color ? new AdaptiveBackgroundImage(SolidColorImage.DataUri(color)) : null,
        };
    }

    /// <summary>One button in its own right-aligned row, since Teams lines up the buttons inside a button group from the left; rows sit close so the stack stays near the header text's height.</summary>
    private static AdaptiveColumnSet RightAlignedButton(AdaptiveOpenUrlAction link) =>
        new([new AdaptiveColumn([new AdaptiveActionSet([link])], AdaptiveColumn.Auto)]) { HorizontalAlignment = RightAlignment, Spacing = ButtonSpacing };
}

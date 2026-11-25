using System.Text.Json.Serialization;

namespace Lightkeeper.Functions.Cards;

/// <summary>The subset of the Adaptive Card schema Lightkeeper renders. Teams channels support schema version 1.5.</summary>
public sealed record AdaptiveCard
{
    public const string SchemaVersion = "1.5";

    public string Type { get; } = "AdaptiveCard";

    [JsonPropertyName("$schema")]
    public string Schema { get; } = "http://adaptivecards.io/schemas/adaptive-card.json";

    public string Version { get; } = SchemaVersion;

    /// <summary>Plain-text summary for clients that cannot render the card; also what the log and tests read as the card's title.</summary>
    public required string FallbackText { get; init; }

    public IReadOnlyList<AdaptiveElement> Body { get; init; } = [];
    public IReadOnlyList<AdaptiveOpenUrlAction> Actions { get; init; } = [];

    /// <summary>Uses the full width of the Teams channel instead of the narrow default.</summary>
    [JsonPropertyName("msteams")]
    public AdaptiveTeamsOptions Teams { get; } = new("Full");
}

public sealed record AdaptiveTeamsOptions(string Width);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(AdaptiveTextBlock), "TextBlock")]
[JsonDerivedType(typeof(AdaptiveContainer), "Container")]
[JsonDerivedType(typeof(AdaptiveFactSet), "FactSet")]
[JsonDerivedType(typeof(AdaptiveColumnSet), "ColumnSet")]
[JsonDerivedType(typeof(AdaptiveActionSet), "ActionSet")]
[JsonDerivedType(typeof(AdaptiveRichTextBlock), "RichTextBlock")]
public abstract record AdaptiveElement;

public sealed record AdaptiveTextBlock(string Text) : AdaptiveElement
{
    public string? Size { get; init; }
    public string? Weight { get; init; }
    public string? Color { get; init; }
    public bool? IsSubtle { get; init; }
    public bool Wrap { get; init; } = true;
    public string? Spacing { get; init; }
    public string? HorizontalAlignment { get; init; }
}

/// <param name="Style">Background style: <c>good</c>, <c>warning</c>, <c>attention</c>, or <c>emphasis</c>.</param>
public sealed record AdaptiveContainer(IReadOnlyList<AdaptiveElement> Items, string? Style = null) : AdaptiveElement
{
    public bool? Bleed { get; init; }
    public AdaptiveBackgroundImage? BackgroundImage { get; init; }
    public bool? Separator { get; init; }
}

/// <summary>Columns side by side; they do not wrap, so each column's text wraps within its own width instead.</summary>
public sealed record AdaptiveColumnSet(IReadOnlyList<AdaptiveColumn> Columns) : AdaptiveElement
{
    public string? Spacing { get; init; }

    /// <summary><c>Right</c> pushes auto-width columns to the right edge.</summary>
    public string? HorizontalAlignment { get; init; }
}

/// <param name="Width"><c>auto</c> fits the content; <c>stretch</c> takes the remaining width.</param>
public sealed record AdaptiveColumn(IReadOnlyList<AdaptiveElement> Items, string Width)
{
    public const string Auto = "auto";
    public const string Stretch = "stretch";

    public string Type { get; } = "Column";
    public string? VerticalContentAlignment { get; init; }

    /// <summary>The gap before this column: <c>None</c>, <c>Small</c>, <c>Default</c>, and larger.</summary>
    public string? Spacing { get; init; }
}

/// <summary>One wrapping paragraph whose runs can each have their own color, unlike a text block, which has one color throughout.</summary>
public sealed record AdaptiveRichTextBlock(IReadOnlyList<AdaptiveTextRun> Inlines) : AdaptiveElement
{
    public string? Spacing { get; init; }
}

public sealed record AdaptiveTextRun(string Text)
{
    public string Type { get; } = "TextRun";
    public string? Color { get; init; }
    public bool? IsSubtle { get; init; }
}

/// <summary>Buttons placed inside the card body rather than at its foot.</summary>
public sealed record AdaptiveActionSet(IReadOnlyList<AdaptiveOpenUrlAction> Actions) : AdaptiveElement;

/// <param name="FillMode"><c>repeat</c> tiles the image, so a one-pixel image fills any size.</param>
public sealed record AdaptiveBackgroundImage(string Url, string FillMode = "repeat");

public sealed record AdaptiveFactSet(IReadOnlyList<AdaptiveFact> Facts) : AdaptiveElement
{
    public bool? Separator { get; init; }
}

public sealed record AdaptiveFact(string Title, string Value);

public sealed record AdaptiveOpenUrlAction(string Title, string Url)
{
    public string Type { get; } = "Action.OpenUrl";
}

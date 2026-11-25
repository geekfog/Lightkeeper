using Lightkeeper.Functions.Models;
using Lightkeeper.Functions.Routing;

namespace Lightkeeper.Functions.Cards;

/// <summary>Turns one service-hook event type into a card. Register one implementation per supported event type.</summary>
public interface IServiceHookCardFormatter
{
    string EventType { get; }

    /// <exception cref="System.Text.Json.JsonException">The resource does not match the event's schema.</exception>
    CardFormatResult Format(ServiceHookEvent serviceHookEvent);
}

/// <param name="Facts">What notification rules match against; set whenever <paramref name="Card"/> or <paramref name="SkipReason"/> is.</param>
/// <param name="SkipReason">Set when the event is valid but not worth a card, such as a draft pull request; logged as <c>Skip (reason)</c>.</param>
/// <param name="AnnounceOnceKey">Set when the card should post only once per subject, such as once per pull request; unique within the organization.</param>
public sealed record CardFormatResult(AdaptiveCard? Card, EventFacts? Facts, IReadOnlyList<string> Errors, string? SkipReason = null, string? AnnounceOnceKey = null)
{
    public static CardFormatResult Success(AdaptiveCard card, EventFacts facts, string? announceOnceKey = null) => new(card, facts, [], AnnounceOnceKey: announceOnceKey);

    public static CardFormatResult Skip(EventFacts facts, string reason) => new(null, facts, [], SkipReason: reason);

    public static CardFormatResult Invalid(IReadOnlyList<string> errors) => new(null, null, errors);
}

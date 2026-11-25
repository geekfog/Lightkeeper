using Lightkeeper.Functions.Cards;

namespace Lightkeeper.Functions.Integrations.TeamsWorkflow;

/// <summary>The request body the Teams Workflow webhook template expects: a message whose attachments each carry one Adaptive Card.</summary>
public sealed record TeamsWorkflowMessage(IReadOnlyList<TeamsWorkflowAttachment> Attachments)
{
    public string Type { get; } = "message";

    public static TeamsWorkflowMessage For(AdaptiveCard card) => new([new TeamsWorkflowAttachment(card)]);
}

public sealed record TeamsWorkflowAttachment(AdaptiveCard Content)
{
    public const string AdaptiveCardContentType = "application/vnd.microsoft.card.adaptive";

    public string ContentType { get; } = AdaptiveCardContentType;
}

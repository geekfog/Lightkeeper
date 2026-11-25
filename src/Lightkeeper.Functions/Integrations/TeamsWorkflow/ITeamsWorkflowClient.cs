using Lightkeeper.Functions.Cards;

namespace Lightkeeper.Functions.Integrations.TeamsWorkflow;

public interface ITeamsWorkflowClient
{
    /// <summary>Submits a card to a Teams Workflow webhook. Acceptance means the workflow queued a run, not that Teams displayed the card.</summary>
    /// <param name="webhookUrl">Secret; never logged.</param>
    Task<WorkflowSubmission> SubmitAsync(Uri webhookUrl, AdaptiveCard card, string correlationId, CancellationToken cancellationToken);
}

public enum WorkflowSubmissionStatus
{
    Accepted,
    Rejected,
    TimedOut,
    Unreachable,
}

/// <param name="StatusCode">HTTP status returned by the workflow, when a response was received.</param>
public sealed record WorkflowSubmission(WorkflowSubmissionStatus Status, int? StatusCode = null);

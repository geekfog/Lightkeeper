using System.Collections.Concurrent;
using Lightkeeper.Functions.Cards;
using Lightkeeper.Functions.Integrations.TeamsWorkflow;

namespace Lightkeeper.Functions.Tests.TestSupport;

/// <summary>Answers every submission with <paramref name="submission"/> unless <see cref="RespondTo"/> sets a URL-specific answer.</summary>
internal sealed class FakeTeamsWorkflowClient(WorkflowSubmission submission) : ITeamsWorkflowClient
{
    public static readonly WorkflowSubmission Accepted = new(WorkflowSubmissionStatus.Accepted, 202);

    private readonly ConcurrentDictionary<Uri, WorkflowSubmission> _responses = new();

    public ConcurrentQueue<(Uri Url, AdaptiveCard Card, string CorrelationId)> Submissions { get; } = new();

    public FakeTeamsWorkflowClient RespondTo(Uri url, WorkflowSubmission response)
    {
        _responses[url] = response;
        return this;
    }

    public Task<WorkflowSubmission> SubmitAsync(Uri webhookUrl, AdaptiveCard card, string correlationId, CancellationToken cancellationToken)
    {
        Submissions.Enqueue((webhookUrl, card, correlationId));
        return Task.FromResult(_responses.TryGetValue(webhookUrl, out var response) ? response : submission);
    }
}

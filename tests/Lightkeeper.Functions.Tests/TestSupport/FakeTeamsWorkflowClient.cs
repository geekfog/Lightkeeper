using System.Collections.Concurrent;
using Lightkeeper.Functions.Cards;
using Lightkeeper.Functions.Integrations.TeamsWorkflow;

namespace Lightkeeper.Functions.Tests.TestSupport;

/// <summary>
/// Answers every submission with <paramref name="submission"/> unless <see cref="RespondTo"/> sets a URL-specific answer, or <see cref="RespondAfter"/>
/// an answer for every submission after the first few.
/// </summary>
internal sealed class FakeTeamsWorkflowClient(WorkflowSubmission submission) : ITeamsWorkflowClient
{
    public static readonly WorkflowSubmission Accepted = new(WorkflowSubmissionStatus.Accepted, 202);

    private readonly ConcurrentDictionary<Uri, WorkflowSubmission> _responses = new();
    private (int Count, WorkflowSubmission Response)? _after;

    public ConcurrentQueue<(Uri Url, AdaptiveCard Card, string CorrelationId)> Submissions { get; } = new();

    public FakeTeamsWorkflowClient RespondTo(Uri url, WorkflowSubmission response)
    {
        _responses[url] = response;
        return this;
    }

    public FakeTeamsWorkflowClient RespondAfter(int count, WorkflowSubmission response)
    {
        _after = (count, response);
        return this;
    }

    public Task<WorkflowSubmission> SubmitAsync(Uri webhookUrl, AdaptiveCard card, string correlationId, CancellationToken cancellationToken)
    {
        Submissions.Enqueue((webhookUrl, card, correlationId));
        if (_after is { } after && Submissions.Count > after.Count) return Task.FromResult(after.Response);
        return Task.FromResult(_responses.TryGetValue(webhookUrl, out var response) ? response : submission);
    }
}

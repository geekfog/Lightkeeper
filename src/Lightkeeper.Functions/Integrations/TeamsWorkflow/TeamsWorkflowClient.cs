using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Lightkeeper.Functions.Cards;
using Lightkeeper.Functions.Diagnostics;
using Lightkeeper.Functions.Models;

namespace Lightkeeper.Functions.Integrations.TeamsWorkflow;

/// <summary>Posts Adaptive Cards to Teams Workflow webhooks. Never logs a webhook URL.</summary>
public sealed partial class TeamsWorkflowClient(HttpClient httpClient, ILogger<TeamsWorkflowClient> logger)
    : ITeamsWorkflowClient
{
    private const int MaxLoggedResponseChars = 512;

    public async Task<WorkflowSubmission> SubmitAsync(Uri webhookUrl, AdaptiveCard card, string correlationId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, webhookUrl)
        {
            Content = JsonContent.Create(TeamsWorkflowMessage.For(card), options: LightkeeperJson.Options),
        };
        request.Headers.Add(CorrelationId.HeaderName, correlationId);

        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            var statusCode = (int)response.StatusCode;

            if (response.IsSuccessStatusCode)
            {
                LogAccepted(logger, statusCode);
                return new WorkflowSubmission(WorkflowSubmissionStatus.Accepted, statusCode);
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            LogRejected(logger, statusCode, body.Length > MaxLoggedResponseChars ? body[..MaxLoggedResponseChars] : body);
            return new WorkflowSubmission(WorkflowSubmissionStatus.Rejected, statusCode);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            LogTimedOut(logger, httpClient.Timeout);
            return new WorkflowSubmission(WorkflowSubmissionStatus.TimedOut);
        }
        catch (HttpRequestException ex)
        {
            LogUnreachable(logger, ex.HttpRequestError, ex.Message);
            return new WorkflowSubmission(WorkflowSubmissionStatus.Unreachable);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Teams Workflow accepted the card with HTTP {StatusCode}.")]
    private static partial void LogAccepted(ILogger logger, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Teams Workflow rejected the card with HTTP {StatusCode}: {ResponseBody}")]
    private static partial void LogRejected(ILogger logger, int statusCode, string responseBody);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Teams Workflow did not respond within {Timeout}.")]
    private static partial void LogTimedOut(ILogger logger, TimeSpan timeout);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Teams Workflow could not be reached ({HttpRequestError}): {Reason}")]
    private static partial void LogUnreachable(ILogger logger, HttpRequestError httpRequestError, string reason);
}

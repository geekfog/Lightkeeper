using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Lightkeeper.Functions.Cards;
using Lightkeeper.Functions.Diagnostics;
using Lightkeeper.Functions.Integrations.TeamsWorkflow;
using Lightkeeper.Functions.Tests.TestSupport;

namespace Lightkeeper.Functions.Tests.Integrations;

public sealed class TeamsWorkflowClientTests
{
    private static readonly Uri WebhookUrl = new("https://workflow.example.test/invoke?sig=secret");

    private static readonly AdaptiveCard Card = CardStyle.Card(CardStyle.Success, "title", "subtitle", [("Name", "Value")], [("Open", "https://example.test/")]);

    [Fact]
    public async Task Accepted_response_posts_adaptive_card_message_with_correlation_header()
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.Accepted);

        var submission = await Submit(handler);

        Assert.Equal(new WorkflowSubmission(WorkflowSubmissionStatus.Accepted, 202), submission);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal(WebhookUrl, handler.LastRequest.RequestUri);
        Assert.Equal("corr-1", Assert.Single(handler.LastRequest.Headers.GetValues(CorrelationId.HeaderName)));

        var json = JsonNode.Parse(handler.LastBody!)!;
        Assert.Equal("message", (string?)json["type"]);
        var attachment = json["attachments"]!.AsArray().Single()!;
        Assert.Equal(TeamsWorkflowAttachment.AdaptiveCardContentType, (string?)attachment["contentType"]);
        var card = attachment["content"]!;
        Assert.Equal("AdaptiveCard", (string?)card["type"]);
        Assert.Equal(AdaptiveCard.SchemaVersion, (string?)card["version"]);
        Assert.Equal("Full", (string?)card["msteams"]!["width"]);
        Assert.Equal("Container", (string?)card["body"]![0]!["type"]);
        Assert.Equal(CardStyle.Success.ContainerStyle, (string?)card["body"]![0]!["style"]);
        Assert.Equal("✅ title", (string?)card["body"]![0]!["items"]![0]!["text"]);
        Assert.Equal("FactSet", (string?)card["body"]![1]!["type"]);
        Assert.Equal("Value", (string?)card["body"]![1]!["facts"]![0]!["value"]);
        Assert.Equal("Action.OpenUrl", (string?)card["actions"]![0]!["type"]);
        Assert.Equal("https://example.test/", (string?)card["actions"]![0]!["url"]);
    }

    [Fact]
    public async Task Error_response_is_rejected_with_status()
    {
        var submission = await Submit(StubHttpMessageHandler.Returning(HttpStatusCode.BadRequest, """{"error":"bad"}"""));

        Assert.Equal(new WorkflowSubmission(WorkflowSubmissionStatus.Rejected, 400), submission);
    }

    [Fact]
    public async Task Slow_response_times_out()
    {
        var submission = await Submit(StubHttpMessageHandler.NeverResponding(), TimeSpan.FromMilliseconds(100));

        Assert.Equal(new WorkflowSubmission(WorkflowSubmissionStatus.TimedOut), submission);
    }

    [Fact]
    public async Task Connection_failure_is_unreachable()
    {
        var submission = await Submit(StubHttpMessageHandler.Throwing(new HttpRequestException(HttpRequestError.NameResolutionError)));

        Assert.Equal(new WorkflowSubmission(WorkflowSubmissionStatus.Unreachable), submission);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_reported_as_timeout()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Submit(StubHttpMessageHandler.NeverResponding(), TimeSpan.FromSeconds(30), cancellation.Token));
    }

    private static Task<WorkflowSubmission> Submit(StubHttpMessageHandler handler, TimeSpan? timeout = null, CancellationToken? cancellationToken = null)
    {
        var httpClient = new HttpClient(handler) { Timeout = timeout ?? TimeSpan.FromSeconds(5) };
        var client = new TeamsWorkflowClient(httpClient, NullLogger<TeamsWorkflowClient>.Instance);
        return client.SubmitAsync(WebhookUrl, Card, "corr-1", cancellationToken ?? TestContext.Current.CancellationToken);
    }
}

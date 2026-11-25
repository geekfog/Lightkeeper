namespace Lightkeeper.Functions.Tests.TestSupport;

/// <summary>Answers outbound requests with a supplied delegate and records the last request and its body.</summary>
internal sealed class StubHttpMessageHandler(Func<CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    public HttpRequestMessage? LastRequest { get; private set; }
    public string? LastBody { get; private set; }

    public static StubHttpMessageHandler Returning(System.Net.HttpStatusCode statusCode, string body = "") =>
        new(_ => Task.FromResult(new HttpResponseMessage(statusCode) { Content = new StringContent(body) }));

    public static StubHttpMessageHandler NeverResponding() =>
        new(async token =>
        {
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException("Unreachable.");
        });

    public static StubHttpMessageHandler Throwing(HttpRequestException exception) => new(_ => throw exception);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        return await respond(cancellationToken);
    }
}

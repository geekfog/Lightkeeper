using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Lightkeeper.Functions.Diagnostics;
using Lightkeeper.Functions.Integrations.TableStorage;
using Lightkeeper.Functions.Notifications;

namespace Lightkeeper.Functions.Triggers;

/// <summary>Receives Azure DevOps service-hook POSTs and records each one in the notification log. Callers authenticate with a function key.</summary>
public sealed partial class AzureDevOpsServiceHookTrigger(
    IServiceHookNotifier notifier,
    INotificationLogStore notificationLog,
    TimeProvider timeProvider,
    ILogger<AzureDevOpsServiceHookTrigger> logger)
{
    public const string FunctionName = "AzureDevOpsServiceHook";
    public const string Route = "service-hooks/azure-devops";

    [Function(FunctionName)]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = Route)] HttpRequest request,
        FunctionContext context,
        CancellationToken cancellationToken)
    {
        var receivedUtc = timeProvider.GetUtcNow();
        var started = timeProvider.GetTimestamp();
        var correlationId = CorrelationId.Resolve(request.Headers[CorrelationId.HeaderName], context.InvocationId);
        request.HttpContext.Response.Headers[CorrelationId.HeaderName] = correlationId;

        using var scope = logger.BeginScope(new Dictionary<string, object> { [CorrelationId.LogScopeKey] = correlationId });

        using var reader = new StreamReader(request.Body);
        var payload = await reader.ReadToEndAsync(cancellationToken);
        var result = await notifier.NotifyAsync(payload, correlationId, cancellationToken);
        var statusCode = ToStatusCode(result.Outcome);

        await WriteLogAsync(
            NotificationLogEntry.Create(result, correlationId, payload, receivedUtc, statusCode, timeProvider.GetElapsedTime(started)), cancellationToken);

        return new ObjectResult(new ServiceHookResponse(result.Outcome.ToString(), result.Detail, correlationId)) { StatusCode = statusCode };
    }

    internal static int ToStatusCode(NotificationOutcome outcome) => outcome switch
    {
        NotificationOutcome.WorkflowAccepted or NotificationOutcome.Skipped => StatusCodes.Status200OK,
        NotificationOutcome.InvalidPayload => StatusCodes.Status400BadRequest,
        NotificationOutcome.WorkflowNotConfigured => StatusCodes.Status500InternalServerError,
        NotificationOutcome.WorkflowTimedOut => StatusCodes.Status504GatewayTimeout,
        _ => StatusCodes.Status502BadGateway,
    };

    /// <summary>A log failure never changes the answer to Azure DevOps.</summary>
    private async Task WriteLogAsync(NotificationLogEntry entry, CancellationToken cancellationToken)
    {
        try
        {
            await notificationLog.WriteAsync(entry, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogWriteFailed(logger, NotificationTables.Log, ex.Message);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not write to the {Table} table: {Reason}")]
    private static partial void LogWriteFailed(ILogger logger, string table, string reason);
}

public sealed record ServiceHookResponse(string Outcome, string Detail, string CorrelationId);

using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Lightkeeper.Functions.Integrations.TableStorage;

namespace Lightkeeper.Functions.Triggers;

/// <summary>Deletes notification log rows older than <see cref="NotificationLogOptions.RetentionDays"/>, once a day.</summary>
public sealed partial class NotificationLogRetentionTrigger(
    INotificationLogStore notificationLog,
    IOptions<NotificationLogOptions> options,
    TimeProvider timeProvider,
    ILogger<NotificationLogRetentionTrigger> logger)
{
    public const string FunctionName = "NotificationLogRetention";

    /// <summary>Daily at 03:15 UTC.</summary>
    public const string Schedule = "0 15 3 * * *";

    [Function(FunctionName)]
    public async Task Run([TimerTrigger(Schedule)] TimerInfo timer, CancellationToken cancellationToken)
    {
        var retentionDays = options.Value.RetentionDays;
        var cutoff = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime).AddDays(-retentionDays);
        var deleted = await notificationLog.DeleteBeforeAsync(cutoff, cancellationToken);
        LogDeleted(logger, deleted, NotificationTables.Log, cutoff, retentionDays);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted {Count} {Table} rows received before {Cutoff} ({RetentionDays}-day retention).")]
    private static partial void LogDeleted(ILogger logger, int count, string table, DateOnly cutoff, int retentionDays);
}

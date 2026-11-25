using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using Lightkeeper.Functions.Notifications;
using Lightkeeper.Functions.Routing;
using static Lightkeeper.Functions.Integrations.TableStorage.NotificationTables;

namespace Lightkeeper.Functions.Integrations.TableStorage;

/// <summary>Azure Table Storage implementation of the rule, workflow, announcement, and log stores.</summary>
public sealed partial class NotificationTableStore(TableServiceClient tableService, ILogger<NotificationTableStore> logger)
    : INotificationRuleStore, INotificationAnnouncementStore, INotificationLogStore
{
    /// <summary>A string property holds at most 64 KiB (32K UTF-16 characters); an entity at most 1 MiB.</summary>
    internal const int MaxPayloadPartChars = 32_000;
    internal const int MaxPayloadParts = 12;
    internal const string PayloadTruncatedColumn = nameof(NotificationLogEntry.Payload) + "Truncated";

    /// <summary>A table transaction holds at most 100 operations, all in one partition.</summary>
    private const int MaxTransactionSize = 100;

    private readonly ConcurrentDictionary<string, Lazy<Task<TableClient>>> _tables = new(StringComparer.Ordinal);

    public async Task<IReadOnlyList<NotificationRule>> GetRulesAsync(CancellationToken cancellationToken)
    {
        var table = await GetTableAsync(Rule, cancellationToken);
        List<NotificationRule> rules = [];
        await foreach (var entity in table.QueryAsync<TableEntity>(cancellationToken: cancellationToken))
        {
            if (!IsActive(entity))
            {
                continue;
            }

            if (TryReadRule(entity, out var rule, out var error))
            {
                rules.Add(rule);
                if (!IsKnownNotificationType(entity))
                {
                    LogUnknownNotificationType(logger, entity.PartitionKey, entity.RowKey, entity.GetString(RuleColumns.NotificationType), rule.NotificationType);
                }
            }
            else
            {
                LogInvalidRule(logger, entity.PartitionKey, entity.RowKey, error);
            }
        }

        return rules;
    }

    public async Task<IReadOnlyDictionary<string, string>> GetWorkflowUrlsAsync(CancellationToken cancellationToken)
    {
        var table = await GetTableAsync(Workflow, cancellationToken);
        var filter = TableClient.CreateQueryFilter($"PartitionKey eq {WorkflowPartition}");
        Dictionary<string, string> urls = new(StringComparer.OrdinalIgnoreCase);
        await foreach (var entity in table.QueryAsync<TableEntity>(filter, cancellationToken: cancellationToken))
        {
            if (entity.GetString(WorkflowColumns.Url) is { Length: > 0 } url)
            {
                urls[entity.RowKey] = url;
            }
        }

        return urls;
    }

    public async Task<bool> TryClaimAsync(
        string organization, string key, NotificationSubject subject, string correlationId, DateTimeOffset announcedUtc, CancellationToken cancellationToken)
    {
        var table = await GetTableAsync(Announcement, cancellationToken);
        var entity = new TableEntity(organization, key)
        {
            [AnnouncementColumns.Trigger] = subject.Trigger,
            [AnnouncementColumns.Project] = subject.Facts.Project,
            [AnnouncementColumns.Reference] = subject.Facts.Reference,
            [AnnouncementColumns.CorrelationId] = correlationId,
            [AnnouncementColumns.AnnouncedUtc] = announcedUtc,
        };
        try
        {
            await table.AddEntityAsync(entity, cancellationToken);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == (int)HttpStatusCode.Conflict)
        {
            return false;
        }
    }

    public async Task<IReadOnlyDictionary<string, DateTimeOffset>> GetAnnouncedTimesAsync(
        string organization, IReadOnlyCollection<string> keys, CancellationToken cancellationToken)
    {
        var table = await GetTableAsync(Announcement, cancellationToken);
        var rows = await Task.WhenAll(keys.Distinct(StringComparer.Ordinal).Select(key =>
            table.GetEntityIfExistsAsync<TableEntity>(organization, key, [AnnouncementColumns.AnnouncedUtc], cancellationToken)));
        return rows.Where(r => r.HasValue).Select(r => r.Value!).Select(e => (e.RowKey, When: AnnouncedAt(e))).Where(r => r.When is not null)
            .ToDictionary(r => r.RowKey, r => r.When!.Value, StringComparer.Ordinal);
    }

    /// <summary>Rows written before <see cref="AnnouncementColumns.AnnouncedUtc"/> existed fall back to their write time.</summary>
    internal static DateTimeOffset? AnnouncedAt(TableEntity entity) => entity.GetDateTimeOffset(AnnouncementColumns.AnnouncedUtc) ?? entity.Timestamp;

    public async Task ReleaseAsync(string organization, string key, CancellationToken cancellationToken)
    {
        var table = await GetTableAsync(Announcement, cancellationToken);
        await table.DeleteEntityAsync(organization, key, ETag.All, cancellationToken);
    }

    public async Task WriteAsync(NotificationLogEntry entry, CancellationToken cancellationToken)
    {
        var table = await GetTableAsync(Log, cancellationToken);
        await table.UpsertEntityAsync(ToLogEntity(entry), TableUpdateMode.Replace, cancellationToken);
    }

    public async Task<int> DeleteBeforeAsync(DateOnly cutoff, CancellationToken cancellationToken)
    {
        var table = await GetTableAsync(Log, cancellationToken);
        var filter = TableClient.CreateQueryFilter($"PartitionKey lt {cutoff.ToString(LogPartitionFormat, CultureInfo.InvariantCulture)}");
        var deleted = 0;
        List<TableTransactionAction> batch = [];

        async Task FlushAsync()
        {
            if (batch.Count == 0) return;
            await table.SubmitTransactionAsync(batch, cancellationToken);
            deleted += batch.Count;
            batch = [];
        }

        await foreach (var entity in table.QueryAsync<TableEntity>(filter, select: [nameof(TableEntity.PartitionKey), nameof(TableEntity.RowKey)],
                           cancellationToken: cancellationToken))
        {
            if (batch.Count == MaxTransactionSize || (batch.Count > 0 && batch[0].Entity.PartitionKey != entity.PartitionKey))
            {
                await FlushAsync();
            }

            batch.Add(new TableTransactionAction(TableTransactionActionType.Delete, entity, ETag.All));
        }

        await FlushAsync();
        return deleted;
    }

    /// <summary>Only an explicit <c>true</c> (Boolean, or the text <c>true</c>) makes a row a rule; missing or anything else leaves it off.</summary>
    internal static bool IsActive(TableEntity entity) =>
        (entity.TryGetValue(RuleColumns.Active, out var raw) ? raw : null) switch
        {
            bool active => active,
            string text => bool.TryParse(text.Trim(), out var active) && active,
            _ => false,
        };

    internal static bool TryReadRule(TableEntity entity, out NotificationRule rule, out string error)
    {
        rule = null!;
        if (!TryReadInt(entity, RuleColumns.Priority, out var priority))
        {
            error = $"{RuleColumns.Priority} must be a whole number.";
            return false;
        }

        // Names only: Enum.TryParse would also accept numbers such as "1".
        var actionName = Enum.GetNames<RuleAction>().FirstOrDefault(n => n.Equals(entity.GetString(RuleColumns.Action)?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (actionName is null)
        {
            error = $"{RuleColumns.Action} must be {string.Join(" or ", Enum.GetNames<RuleAction>())}.";
            return false;
        }

        rule = new NotificationRule(
            entity.PartitionKey,
            entity.RowKey,
            entity.GetString(RuleColumns.Project),
            priority,
            entity.GetString(RuleColumns.Trigger),
            entity.GetString(RuleColumns.Folder),
            entity.GetString(RuleColumns.Pipeline),
            entity.GetString(RuleColumns.Status),
            Enum.Parse<RuleAction>(actionName),
            WorkflowIds.Parse(entity.GetString(RuleColumns.Workflow)),
            entity.GetString(RuleColumns.Note),
            ReadNotificationType(entity) ?? default);
        error = string.Empty;
        return true;
    }

    /// <summary>The named <see cref="NotificationType"/>, ignoring case and surrounding spaces; null when empty or unrecognized. Names only, not numbers.</summary>
    internal static NotificationType? ReadNotificationType(TableEntity entity)
    {
        var text = entity.GetString(RuleColumns.NotificationType)?.Trim();
        var name = Enum.GetNames<NotificationType>().FirstOrDefault(n => n.Equals(text, StringComparison.OrdinalIgnoreCase));
        return name is null ? null : Enum.Parse<NotificationType>(name);
    }

    /// <summary>Empty is a deliberate default; any other value that is not a type name is a typo worth a warning.</summary>
    private static bool IsKnownNotificationType(TableEntity entity) =>
        string.IsNullOrWhiteSpace(entity.GetString(RuleColumns.NotificationType)) || ReadNotificationType(entity) is not null;

    internal static TableEntity ToLogEntity(NotificationLogEntry entry)
    {
        var received = entry.ReceivedUtc.UtcDateTime;
        var entity = new TableEntity(
            received.ToString(LogPartitionFormat, CultureInfo.InvariantCulture),
            $"{received.ToString(LogRowTimeFormat, CultureInfo.InvariantCulture)}-{entry.CorrelationId}")
        {
            [nameof(entry.ReceivedUtc)] = entry.ReceivedUtc,
            [nameof(entry.CorrelationId)] = entry.CorrelationId,
            [nameof(entry.Trigger)] = entry.Trigger,
            [nameof(entry.Organization)] = entry.Organization,
            [nameof(entry.Project)] = entry.Project,
            [nameof(entry.Pipeline)] = entry.Pipeline,
            [nameof(entry.Reference)] = entry.Reference,
            [nameof(entry.Status)] = entry.Status,
            [nameof(entry.RuleId)] = entry.RuleId,
            [nameof(entry.RulePriority)] = entry.RulePriority,
            [nameof(entry.RuleAction)] = entry.RuleAction,
            [nameof(entry.Decision)] = entry.Decision,
            [nameof(entry.Workflow)] = entry.Workflow,
            [nameof(entry.Outcome)] = entry.Outcome,
            [nameof(entry.HttpStatus)] = entry.HttpStatus,
            [nameof(entry.Detail)] = entry.Detail,
            [nameof(entry.DurationMs)] = entry.DurationMs,
        };

        // Payload, Payload2, Payload3, ...: one column per part, in order.
        var payload = entry.Payload;
        for (var part = 0; part < MaxPayloadParts && part * MaxPayloadPartChars < payload.Length; part++)
        {
            var start = part * MaxPayloadPartChars;
            var column = part == 0 ? nameof(entry.Payload) : $"{nameof(entry.Payload)}{part + 1}";
            entity[column] = payload.Substring(start, Math.Min(MaxPayloadPartChars, payload.Length - start));
        }

        if (payload.Length > MaxPayloadParts * MaxPayloadPartChars)
        {
            entity[PayloadTruncatedColumn] = true;
        }

        return entity;
    }

    private static bool TryReadInt(TableEntity entity, string column, out int value)
    {
        value = 0;
        switch (entity.TryGetValue(column, out var raw) ? raw : null)
        {
            case int number:
                value = number;
                return true;
            case long number when number is >= int.MinValue and <= int.MaxValue:
                value = (int)number;
                return true;
            case string text:
                return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
            default:
                return false;
        }
    }

    /// <summary>Creates each table once per process; a failed creation is retried on the next call.</summary>
    private async Task<TableClient> GetTableAsync(string name, CancellationToken cancellationToken)
    {
        var lazy = _tables.GetOrAdd(name, n => new Lazy<Task<TableClient>>(async () =>
        {
            var client = tableService.GetTableClient(n);
            await client.CreateIfNotExistsAsync(CancellationToken.None);
            return client;
        }));

        try
        {
            return await lazy.Value.WaitAsync(cancellationToken);
        }
        catch (RequestFailedException)
        {
            _tables.TryRemove(new KeyValuePair<string, Lazy<Task<TableClient>>>(name, lazy));
            throw;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ignoring notification rule {Organization}/{RuleId}: {Error}")]
    private static partial void LogInvalidRule(ILogger logger, string organization, string ruleId, string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Notification rule {Organization}/{RuleId} has unrecognized NotificationType '{Value}', so it posts {Default}.")]
    private static partial void LogUnknownNotificationType(ILogger logger, string organization, string ruleId, string? value, NotificationType @default);
}

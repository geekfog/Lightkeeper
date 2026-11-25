using System.ComponentModel.DataAnnotations;

namespace Lightkeeper.Functions.Integrations.TableStorage;

/// <summary>Table and column names. The tables live in the Function App's host storage account and are created on first use.</summary>
public static class NotificationTables
{
    public const string Rule = "NotificationRule";
    public const string Workflow = "TeamsWorkflow";
    public const string Log = "NotificationLog";

    /// <summary>PartitionKey is the organization, RowKey the formatter's announce-once key.</summary>
    public const string Announcement = "NotificationAnnouncement";

    /// <summary>PartitionKey of every <see cref="Workflow"/> row.</summary>
    public const string WorkflowPartition = "workflow";

    /// <summary><see cref="Log"/> PartitionKey: the UTC date, so a day can be browsed or deleted as a unit.</summary>
    public const string LogPartitionFormat = "yyyy-MM-dd";

    /// <summary><see cref="Log"/> RowKey prefix: the UTC time, followed by <c>-</c> and the correlation ID.</summary>
    public const string LogRowTimeFormat = "HH:mm:ss.fffffff";

    public static class RuleColumns
    {
        public const string Active = "Active";
        public const string Project = "Project";
        public const string Priority = "Priority";
        public const string Trigger = "Trigger";
        public const string Folder = "Folder";
        public const string Pipeline = "Pipeline";
        public const string Status = "Status";
        public const string Action = "Action";
        public const string Workflow = "Workflow";
        public const string Note = "Note";
        public const string NotificationType = "NotificationType";
    }

    public static class AnnouncementColumns
    {
        public const string Trigger = "Trigger";
        public const string Project = "Project";
        public const string Reference = "Reference";
        public const string CorrelationId = "CorrelationId";

        /// <summary>When the subject was announced; for a pull request, when it became ready for review.</summary>
        public const string AnnouncedUtc = "AnnouncedUtc";
    }

    public static class WorkflowColumns
    {
        public const string Channel = "Channel";
        public const string Url = "Url";
    }
}

public sealed class NotificationLogOptions
{
    public const string SectionName = "NotificationLog";

    [Range(1, 3650)]
    public int RetentionDays { get; set; } = 90;
}

using Azure.Data.Tables;
using Lightkeeper.Functions.Integrations.TableStorage;
using Lightkeeper.Functions.Notifications;
using Lightkeeper.Functions.Routing;
using static Lightkeeper.Functions.Integrations.TableStorage.NotificationTables;

namespace Lightkeeper.Functions.Tests.Integrations;

/// <summary>Row mapping only; reads and writes against Table Storage are checked by running the function against Azurite.</summary>
public sealed class NotificationTableStoreTests
{
    private static readonly string[] FirstThreePayloadColumns = ["Payload", "Payload2", "Payload3"];

    [Theory]
    [InlineData(30)]
    [InlineData(30L)]
    [InlineData("30")]
    public void Rule_row_reads_priority_typed_as_number_or_text(object priority)
    {
        var entity = RuleEntity(priority, "include");
        entity[RuleColumns.Workflow] = "team-prd; team-qa";

        Assert.True(NotificationTableStore.TryReadRule(entity, out var rule, out _));
        Assert.Equal(30, rule.Priority);
        Assert.Equal(RuleAction.Include, rule.Action);
        Assert.Equal(["team-prd", "team-qa"], rule.Workflows);
        Assert.Equal("fabrikam", rule.Organization);
        Assert.Equal("prd-failures", rule.RuleId);
    }

    [Theory]
    [InlineData(null, "Include")]
    [InlineData("high", "Include")]
    [InlineData(30, "Notify")]
    [InlineData(30, null)]
    [InlineData(30, "2")]
    [InlineData(30, "1")]
    public void Rule_row_without_valid_priority_or_action_is_rejected(object? priority, string? action)
    {
        Assert.False(NotificationTableStore.TryReadRule(RuleEntity(priority, action), out _, out var error));
        Assert.NotEmpty(error);
    }

    [Theory]
    [InlineData("ActiveList", NotificationType.ActiveList)]
    [InlineData(" activelist ", NotificationType.ActiveList)]
    [InlineData("Card", NotificationType.Card)]
    [InlineData(null, NotificationType.Card)]
    [InlineData("", NotificationType.Card)]
    [InlineData("list", NotificationType.Card)]
    [InlineData("1", NotificationType.Card)]
    public void Notification_type_defaults_to_card_unless_a_known_type_is_named(string? value, NotificationType expected)
    {
        var entity = RuleEntity(30, "Include");
        entity[RuleColumns.NotificationType] = value;

        Assert.True(NotificationTableStore.TryReadRule(entity, out var rule, out _));
        Assert.Equal(expected, rule.NotificationType);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData("true", true)]
    [InlineData(" TRUE ", true)]
    [InlineData(false, false)]
    [InlineData("false", false)]
    [InlineData("yes", false)]
    [InlineData(1, false)]
    [InlineData(null, false)]
    public void Only_an_explicit_true_makes_a_row_active(object? active, bool expected) =>
        Assert.Equal(expected, NotificationTableStore.IsActive(new TableEntity("fabrikam", "r") { [RuleColumns.Active] = active }));

    [Fact]
    public void Row_without_active_column_is_inactive() =>
        Assert.False(NotificationTableStore.IsActive(new TableEntity("fabrikam", "r")));

    [Fact]
    public void Announced_time_reads_its_column_and_falls_back_to_when_the_row_was_written()
    {
        var written = new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.Zero);
        var announced = written.AddDays(-3);

        Assert.Equal(announced, NotificationTableStore.AnnouncedAt(new TableEntity("fabrikam", "k") { Timestamp = written, [AnnouncementColumns.AnnouncedUtc] = announced }));
        Assert.Equal(written, NotificationTableStore.AnnouncedAt(new TableEntity("fabrikam", "k") { Timestamp = written }));
    }

    [Fact]
    public void Log_row_is_keyed_by_utc_date_and_time_with_correlation_id()
    {
        var entity = NotificationTableStore.ToLogEntity(Entry(new DateTimeOffset(2026, 11, 21, 23, 30, 5, TimeSpan.FromHours(-5)), "{}"));

        Assert.Equal("2026-11-22", entity.PartitionKey);
        Assert.StartsWith("04:30:05.", entity.RowKey, StringComparison.Ordinal);
        Assert.EndsWith("-corr-1", entity.RowKey, StringComparison.Ordinal);
        Assert.Equal("{}", entity.GetString(nameof(NotificationLogEntry.Payload)));
        Assert.False(entity.ContainsKey(NotificationTableStore.PayloadTruncatedColumn));
    }

    [Fact]
    public void Large_payload_continues_across_columns_without_loss()
    {
        var payload = new string('x', NotificationTableStore.MaxPayloadPartChars * 2 + 10);

        var entity = NotificationTableStore.ToLogEntity(Entry(DateTimeOffset.UtcNow, payload));

        var parts = FirstThreePayloadColumns.Select(entity.GetString).ToArray();
        Assert.Equal(payload, string.Concat(parts));
        Assert.All(parts, p => Assert.True(p.Length <= NotificationTableStore.MaxPayloadPartChars));
        Assert.False(entity.ContainsKey("Payload4"));
    }

    [Fact]
    public void Payload_beyond_the_row_limit_is_marked_truncated()
    {
        var payload = new string('x', NotificationTableStore.MaxPayloadPartChars * NotificationTableStore.MaxPayloadParts + 1);

        var entity = NotificationTableStore.ToLogEntity(Entry(DateTimeOffset.UtcNow, payload));

        Assert.True(entity.GetBoolean(NotificationTableStore.PayloadTruncatedColumn));
    }

    private static TableEntity RuleEntity(object? priority, string? action) =>
        new("fabrikam", "prd-failures") { [RuleColumns.Priority] = priority, [RuleColumns.Action] = action };

    private static NotificationLogEntry Entry(DateTimeOffset received, string payload) =>
        new(received, "corr-1", null, null, null, null, null, null, null, null, null, "", null, "InvalidPayload", 400, "detail", 1, payload);
}

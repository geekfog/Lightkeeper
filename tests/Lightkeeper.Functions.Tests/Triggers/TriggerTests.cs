using Lightkeeper.Functions.Diagnostics;
using Lightkeeper.Functions.Notifications;
using Lightkeeper.Functions.Triggers;

namespace Lightkeeper.Functions.Tests.Triggers;

public sealed class TriggerTests
{
    [Fact]
    public void Only_acceptance_and_intentional_skips_report_success_to_Azure_DevOps()
    {
        foreach (var outcome in Enum.GetValues<NotificationOutcome>())
        {
            var statusCode = AzureDevOpsServiceHookTrigger.ToStatusCode(outcome);
            Assert.Equal(outcome is NotificationOutcome.WorkflowAccepted or NotificationOutcome.Skipped, statusCode is >= 200 and < 300);
        }
    }

    [Theory]
    [InlineData("abc-123_X.y", "abc-123_X.y")]
    [InlineData(null, "fallback")]
    [InlineData("line\nbreak", "fallback")]
    public void Correlation_id_accepts_only_safe_caller_values(string? callerValue, string expected) =>
        Assert.Equal(expected, CorrelationId.Resolve(callerValue, "fallback"));

    [Fact]
    public void Correlation_id_rejects_oversized_caller_value() =>
        Assert.Equal("fallback", CorrelationId.Resolve(new string('a', 65), "fallback"));
}

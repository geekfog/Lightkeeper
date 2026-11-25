using System.Text.RegularExpressions;

namespace Lightkeeper.Functions.Diagnostics;

/// <summary>Correlation ID accepted from callers, echoed in responses, and forwarded to downstream integrations.</summary>
public static partial class CorrelationId
{
    public const string HeaderName = "x-correlation-id";
    public const string LogScopeKey = "CorrelationId";

    /// <summary>Uses the caller's ID when it is safe to log and forward; otherwise the fallback (the Functions invocation ID).</summary>
    public static string Resolve(string? callerValue, string fallback) =>
        callerValue is not null && SafeValue().IsMatch(callerValue) ? callerValue : fallback;

    [GeneratedRegex("^[A-Za-z0-9._-]{1,64}$")]
    private static partial Regex SafeValue();
}

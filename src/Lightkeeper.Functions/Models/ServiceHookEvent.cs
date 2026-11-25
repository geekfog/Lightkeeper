using System.Text.Json;

namespace Lightkeeper.Functions.Models;

/// <summary>Envelope shared by every Azure DevOps service-hook event. <see cref="Resource"/> is parsed by the formatter for <see cref="EventType"/>.</summary>
public sealed record ServiceHookEvent(
    string? Id,
    string? EventType,
    string? PublisherId,
    string? SubscriptionId,
    int? NotificationId,
    ServiceHookMessage? Message,
    ServiceHookMessage? DetailedMessage,
    JsonElement Resource,
    string? ResourceVersion,
    ResourceContainers? ResourceContainers,
    DateTimeOffset? CreatedDate);

public sealed record ResourceContainers(ResourceContainer? Collection, ResourceContainer? Account, ResourceContainer? Project);

public sealed record ResourceContainer(string? Id, string? BaseUrl);

public sealed record ServiceHookMessage(string? Text, string? Markdown);

/// <summary>Service-hook event IDs, as listed in the Azure DevOps service-hook event reference.</summary>
public static class ServiceHookEventTypes
{
    public const string BuildCompleted = "build.complete";
    public const string PullRequestCreated = "git.pullrequest.created";
    public const string PullRequestUpdated = "git.pullrequest.updated";

    /// <summary>Every <c>git.pullrequest.*</c> event starts with this.</summary>
    public const string PullRequestPrefix = "git.pullrequest.";

    public static bool IsPullRequest(string eventType) => eventType.StartsWith(PullRequestPrefix, StringComparison.OrdinalIgnoreCase);
}

using System.ComponentModel.DataAnnotations;

namespace Lightkeeper.Functions.Integrations.TeamsWorkflow;

/// <summary>Settings for Teams Workflow webhooks. <see cref="FallbackWebhookUrl"/> is the fallback workflow; it carries a signature and must be treated as a secret.</summary>
public sealed class TeamsWorkflowOptions
{
    public const string SectionName = "TeamsWorkflow";
    public const string HttpsRequiredMessage = $"{SectionName}:{nameof(FallbackWebhookUrl)} must be an absolute https URL.";

    [Required]
    public Uri? FallbackWebhookUrl { get; set; }

    [Range(typeof(TimeSpan), "00:00:01", "00:02:00")]
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(15);

    public static bool HasHttpsFallbackWebhookUrl(TeamsWorkflowOptions options) => IsHttpsUrl(options.FallbackWebhookUrl);

    /// <summary>Every Teams Workflow URL, from settings or the <c>TeamsWorkflow</c> table, must pass this.</summary>
    public static bool IsHttpsUrl(Uri? url) => url is { IsAbsoluteUri: true } && url.Scheme == Uri.UriSchemeHttps;
}

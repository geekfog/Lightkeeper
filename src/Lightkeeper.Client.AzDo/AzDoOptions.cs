using System.ComponentModel.DataAnnotations;

namespace Lightkeeper.Client.AzDo;

/// <summary>Settings for Azure DevOps REST calls. <see cref="PersonalAccessToken"/> is a secret; while it is empty every call returns <see cref="AzDoResultStatus.NotConfigured"/>.</summary>
public sealed class AzDoOptions
{
    public const string SectionName = "AzDo";

    /// <summary>Needs the <c>Code (Read)</c> scope in every organization Lightkeeper lists pull requests for. Never logged.</summary>
    public string? PersonalAccessToken { get; set; }

    [Range(typeof(TimeSpan), "00:00:01", "00:02:00")]
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    public bool IsConfigured => !string.IsNullOrWhiteSpace(PersonalAccessToken);
}

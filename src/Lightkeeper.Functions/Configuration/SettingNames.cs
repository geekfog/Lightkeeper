namespace Lightkeeper.Functions.Configuration;

/// <summary>Application setting names read outside an options class.</summary>
internal static class SettingNames
{
    public const string ApplicationInsightsConnectionString = "APPLICATIONINSIGHTS_CONNECTION_STRING";

    /// <summary>Key Vault whose secrets are loaded as settings at startup; a secret named <c>AzDo--PersonalAccessToken</c> becomes <c>AzDo:PersonalAccessToken</c>. Empty skips Key Vault.</summary>
    public const string KeyVaultUri = "KeyVault:Uri";
}

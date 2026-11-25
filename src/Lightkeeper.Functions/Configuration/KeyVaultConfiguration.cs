using Azure.Identity;
using Microsoft.Extensions.Configuration;

namespace Lightkeeper.Functions.Configuration;

public static class KeyVaultConfiguration
{
    /// <summary>
    /// Adds every secret in the vault named by <see cref="SettingNames.KeyVaultUri"/> as a setting, read once at startup with the Function App's system-assigned managed identity.
    /// Secrets override app settings of the same name. Does nothing when the setting is empty.
    /// </summary>
    public static IConfigurationBuilder AddLightkeeperKeyVault(this IConfigurationManager configuration)
    {
        var vault = configuration[SettingNames.KeyVaultUri];
        if (string.IsNullOrWhiteSpace(vault))
        {
            return configuration;
        }

        if (!Uri.TryCreate(vault, UriKind.Absolute, out var vaultUri) || vaultUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException($"{SettingNames.KeyVaultUri} must be an absolute https URL, such as https://<vault>.vault.azure.net/.");
        }

        return configuration.AddAzureKeyVault(vaultUri, new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned));
    }
}

using Lightkeeper.Client.AzDo;

namespace Lightkeeper.Functions.Models;

/// <summary>Reads the Azure DevOps organization name from the base URL that service-hook events carry.</summary>
public static class AzureDevOpsOrganization
{
    private const string LegacyHostSuffix = ".visualstudio.com";

    public static string? From(ServiceHookEvent serviceHookEvent) =>
        FromBaseUrl(serviceHookEvent.ResourceContainers?.Account?.BaseUrl ?? serviceHookEvent.ResourceContainers?.Collection?.BaseUrl);

    /// <summary>Handles <c>https://dev.azure.com/{org}/</c> and the older <c>https://{org}.visualstudio.com/</c>.</summary>
    public static string? FromBaseUrl(string? baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            return null;
        }

        if (uri.Host.Equals(AzDoUrls.ServicesHost, StringComparison.OrdinalIgnoreCase))
        {
            var segment = uri.AbsolutePath.Trim('/').Split('/')[0];
            return segment.Length > 0 ? Uri.UnescapeDataString(segment) : null;
        }

        return uri.Host.EndsWith(LegacyHostSuffix, StringComparison.OrdinalIgnoreCase) ? uri.Host[..^LegacyHostSuffix.Length] : null;
    }
}

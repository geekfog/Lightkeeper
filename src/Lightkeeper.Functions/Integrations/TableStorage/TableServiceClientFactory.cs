using Azure.Data.Tables;
using Azure.Identity;
using Microsoft.Extensions.Configuration;

namespace Lightkeeper.Functions.Integrations.TableStorage;

/// <summary>Connects to the same storage account as the Functions host, using the host's own <c>AzureWebJobsStorage</c> settings.</summary>
public static class TableServiceClientFactory
{
    public const string ConnectionName = "AzureWebJobsStorage";
    private const string TableServiceUriKey = "tableServiceUri";
    private const string ClientIdKey = "clientId";

    /// <summary>A connection string (Azurite locally), or <c>__tableServiceUri</c> with the managed identity (hosted).</summary>
    public static TableServiceClient Create(IConfiguration configuration)
    {
        var section = configuration.GetSection(ConnectionName);
        if (!string.IsNullOrWhiteSpace(section.Value))
        {
            return new TableServiceClient(section.Value);
        }

        var serviceUri = section[TableServiceUriKey];
        if (string.IsNullOrWhiteSpace(serviceUri))
        {
            throw new InvalidOperationException($"{ConnectionName} needs a connection string or {ConnectionName}__{TableServiceUriKey}.");
        }

        var clientId = section[ClientIdKey];
        var identity = string.IsNullOrWhiteSpace(clientId) ? ManagedIdentityId.SystemAssigned : ManagedIdentityId.FromUserAssignedClientId(clientId);
        return new TableServiceClient(new Uri(serviceUri), new ManagedIdentityCredential(identity));
    }
}

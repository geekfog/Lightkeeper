using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Lightkeeper.Client.AzDo;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers <see cref="IAzDoClient"/> as a typed <see cref="HttpClient"/> bound to the <see cref="AzDoOptions.SectionName"/> section.</summary>
    public static IServiceCollection AddAzDoClient(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AzDoOptions>()
            .Bind(configuration.GetSection(AzDoOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpClient<IAzDoClient, AzDoClient>((provider, client) =>
            client.Timeout = provider.GetRequiredService<IOptions<AzDoOptions>>().Value.Timeout);

        return services;
    }
}

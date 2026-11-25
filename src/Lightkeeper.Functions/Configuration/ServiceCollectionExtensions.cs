using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Lightkeeper.Client.AzDo;
using Lightkeeper.Functions.Cards;
using Lightkeeper.Functions.Integrations.TableStorage;
using Lightkeeper.Functions.Integrations.TeamsWorkflow;
using Lightkeeper.Functions.Models;
using Lightkeeper.Functions.Notifications;

namespace Lightkeeper.Functions.Configuration;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers notification orchestration, card formatters, the Teams Workflow and Azure DevOps clients, and the notification tables.</summary>
    public static IServiceCollection AddLightkeeperNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<TeamsWorkflowOptions>()
            .Bind(configuration.GetSection(TeamsWorkflowOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(TeamsWorkflowOptions.HasHttpsFallbackWebhookUrl, TeamsWorkflowOptions.HttpsRequiredMessage)
            .ValidateOnStart();

        services.AddOptions<NotificationLogOptions>()
            .Bind(configuration.GetSection(NotificationLogOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Built-in HttpClient logging writes the request URI, which for a Teams Workflow contains its signature secret.
        services.AddHttpClient<ITeamsWorkflowClient, TeamsWorkflowClient>((provider, client) =>
            {
                var options = provider.GetRequiredService<IOptions<TeamsWorkflowOptions>>().Value;
                client.Timeout = options.Timeout;
            })
            .RemoveAllLoggers();

        services.AddAzDoClient(configuration);

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(_ => TableServiceClientFactory.Create(configuration));
        services.AddSingleton<NotificationTableStore>();
        services.AddSingleton<INotificationRuleStore>(provider => provider.GetRequiredService<NotificationTableStore>());
        services.AddSingleton<INotificationAnnouncementStore>(provider => provider.GetRequiredService<NotificationTableStore>());
        services.AddSingleton<INotificationLogStore>(provider => provider.GetRequiredService<NotificationTableStore>());

        services.AddSingleton<IServiceHookCardFormatter, BuildCompletedCardFormatter>();
        services.AddSingleton<IServiceHookCardFormatter>(new PullRequestCardFormatter(ServiceHookEventTypes.PullRequestCreated));
        services.AddSingleton<IServiceHookCardFormatter>(new PullRequestCardFormatter(ServiceHookEventTypes.PullRequestUpdated));
        services.AddSingleton<IServiceHookCardFormatter, AnyEventCardFormatter>();
        // Transient so each use gets a fresh typed HttpClient rather than holding one for the process lifetime.
        services.AddTransient<IServiceHookNotifier, ServiceHookNotifier>();

        return services;
    }
}

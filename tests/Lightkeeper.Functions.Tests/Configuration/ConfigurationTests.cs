using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Lightkeeper.Client.AzDo;
using Lightkeeper.Functions.Configuration;
using Lightkeeper.Functions.Integrations.TableStorage;
using Lightkeeper.Functions.Integrations.TeamsWorkflow;
using Lightkeeper.Functions.Notifications;

namespace Lightkeeper.Functions.Tests.Configuration;

public sealed class ConfigurationTests
{
    private const string WebhookUrlKey = $"{TeamsWorkflowOptions.SectionName}:{nameof(TeamsWorkflowOptions.FallbackWebhookUrl)}";
    private const string TimeoutKey = $"{TeamsWorkflowOptions.SectionName}:{nameof(TeamsWorkflowOptions.Timeout)}";
    private const string RetentionKey = $"{NotificationLogOptions.SectionName}:{nameof(NotificationLogOptions.RetentionDays)}";
    private const string ValidUrl = "https://workflow.example.test/invoke?sig=x";

    [Fact]
    public void Valid_settings_resolve_notifier_and_bind_timeout()
    {
        using var provider = Build(new() { [WebhookUrlKey] = ValidUrl, [TimeoutKey] = "00:00:20" });

        Assert.NotNull(provider.GetRequiredService<IServiceHookNotifier>());
        Assert.NotNull(provider.GetRequiredService<INotificationLogStore>());
        Assert.Equal(TimeSpan.FromSeconds(20), provider.GetRequiredService<IOptions<TeamsWorkflowOptions>>().Value.Timeout);
        Assert.Equal(90, provider.GetRequiredService<IOptions<NotificationLogOptions>>().Value.RetentionDays);
        Assert.NotNull(provider.GetRequiredService<IAzDoClient>());
        Assert.False(provider.GetRequiredService<IOptions<AzDoOptions>>().Value.IsConfigured);
    }

    [Fact]
    public void Azure_devops_timeout_outside_range_fails_validation()
    {
        using var provider = Build(new() { [WebhookUrlKey] = ValidUrl, [$"{AzDoOptions.SectionName}:{nameof(AzDoOptions.Timeout)}"] = "00:05:00" });

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<AzDoOptions>>().Value);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("http://workflow.example.test/invoke", null)]
    public void Invalid_settings_fail_validation(string? webhookUrl, string? timeout)
    {
        using var provider = Build(new() { [WebhookUrlKey] = webhookUrl, [TimeoutKey] = timeout });

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<TeamsWorkflowOptions>>().Value);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("3651")]
    public void Retention_outside_range_fails_validation(string days)
    {
        using var provider = Build(new() { [WebhookUrlKey] = ValidUrl, [RetentionKey] = days });

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<NotificationLogOptions>>().Value);
    }

    private static ServiceProvider Build(Dictionary<string, string?> settings)
    {
        settings[TableServiceClientFactory.ConnectionName] = "UseDevelopmentStorage=true";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new ServiceCollection().AddLogging().AddLightkeeperNotifications(configuration).BuildServiceProvider();
    }
}

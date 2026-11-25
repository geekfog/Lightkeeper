using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Azure.Functions.Worker.OpenTelemetry;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using Lightkeeper.Functions.Configuration;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();
builder.Configuration.AddLightkeeperKeyVault();
builder.Services.AddLightkeeperNotifications(builder.Configuration);

if (!string.IsNullOrWhiteSpace(builder.Configuration[SettingNames.ApplicationInsightsConnectionString]))
{
    builder.Services.AddOpenTelemetry()
        .UseFunctionsWorkerDefaults()
        .UseAzureMonitorExporter();
}

builder.Build().Run();

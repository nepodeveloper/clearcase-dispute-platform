using ClearCase.Api.Disputes;
using ClearCase.Infrastructure.Disputes;

namespace ClearCase.Api.Hosting;

public static class BackgroundWorkExtensions
{
    /// <summary>
    /// Hosted workers never run under the Testing environment. The outbox publisher can be switched off
    /// (BackgroundWorkers:Outbox=false) so API replicas and a dedicated worker replica can share one image.
    /// </summary>
    public static WebApplicationBuilder AddClearCaseBackgroundWork(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        var workersAllowed = !builder.Environment.IsEnvironment("Testing");

        if (workersAllowed && (builder.Configuration.GetValue<bool?>("BackgroundWorkers:Outbox") ?? true))
            services.AddHostedService<OutboxPublisherBackgroundService>();
        if (workersAllowed && builder.Configuration.GetValue<bool>("FinancialOperations:WorkerEnabled"))
            services.AddHostedService<FinancialOperationBackgroundService>();
        if (builder.Configuration.GetValue<bool>("Messaging:Kafka:Enabled"))
        {
            services.AddKafkaOutbox(builder.Configuration);
            if (workersAllowed)
                services.AddHostedService<KafkaConsumerBackgroundService>();
        }
        return builder;
    }
}

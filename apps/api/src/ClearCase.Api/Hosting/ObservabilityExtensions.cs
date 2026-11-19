using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace ClearCase.Api.Hosting;

public static class ObservabilityExtensions
{
    /// <summary>
    /// Registers OpenTelemetry traces and metrics. Data is exported only when an OTLP endpoint is
    /// configured (OTEL_EXPORTER_OTLP_ENDPOINT or Observability:OtlpEndpoint), so local runs stay quiet.
    /// </summary>
    public static WebApplicationBuilder AddClearCaseObservability(this WebApplicationBuilder builder)
    {
        var endpoint = builder.Configuration["Observability:OtlpEndpoint"]
            ?? builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];

        var telemetry = builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(
                builder.Configuration["Observability:ServiceName"] ?? "clearcase-api"))
            .WithTracing(tracing =>
            {
                tracing.AddAspNetCoreInstrumentation(options =>
                    options.Filter = context => !context.Request.Path.StartsWithSegments("/health"));
                tracing.AddHttpClientInstrumentation();
            })
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation();
                metrics.AddHttpClientInstrumentation();
                metrics.AddRuntimeInstrumentation();
                metrics.AddMeter("ClearCase.Outbox", "ClearCase.Kafka");
            });

        if (!string.IsNullOrWhiteSpace(endpoint))
            telemetry.UseOtlpExporter();
        return builder;
    }
}

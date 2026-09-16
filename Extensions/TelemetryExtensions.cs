using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Inventory.Msv.Extensions
{
    public static class TelemetryExtensions
    {
        public static IServiceCollection AddInventoryTelemetry(this IServiceCollection services, IConfiguration configuration)
        {
            var serviceName = "InventoryService";

            services.AddOpenTelemetry()
                .ConfigureResource(resource => resource.AddService(serviceName))
                .WithTracing(tracing =>
                {
                    tracing
                        .AddAspNetCoreInstrumentation(options => options.RecordException = true) 
                        .AddHttpClientInstrumentation(options => options.RecordException = true)
                        .AddSource("MassTransit")
                        .AddOtlpExporter(options =>
                        {
                            options.Endpoint = new Uri(configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] ?? "http://localhost:4317");
                        });
                });

            return services;
        }
    }
}

using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Common;

public static class Telemetry
{
  public static void AddOtel(this IHostApplicationBuilder builder, string serviceName)
  {
    var otlp = Env.Get("OTEL_EXPORTER_OTLP_ENDPOINT", "");
    builder.Services.AddOpenTelemetry()
      .ConfigureResource(r => r.AddService(serviceName))
      .WithTracing(t =>
      {
        t.AddAspNetCoreInstrumentation()
         .AddHttpClientInstrumentation()
         .AddRuntimeInstrumentation();
        if (!string.IsNullOrWhiteSpace(otlp))
          t.AddOtlpExporter(o => o.Endpoint = new Uri(otlp));
      })
      .WithMetrics(m =>
      {
        m.AddAspNetCoreInstrumentation()
         .AddHttpClientInstrumentation()
         .AddRuntimeInstrumentation();
        if (!string.IsNullOrWhiteSpace(otlp))
          m.AddOtlpExporter(o => o.Endpoint = new Uri(otlp));
      });
  }
}

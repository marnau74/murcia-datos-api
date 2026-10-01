using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace MurciaDatos.Api;

public static class Telemetria
{
    /// <summary>
    /// Trazas y métricas con OpenTelemetry. Solo se exportan si hay un colector configurado
    /// (<c>OTEL_EXPORTER_OTLP_ENDPOINT</c>); sin él, no se hace nada con ellas.
    /// </summary>
    public static IServiceCollection ConfigurarTelemetria(this IServiceCollection servicios, IConfiguration configuracion)
    {
        var exportar = !string.IsNullOrWhiteSpace(configuracion["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        var otel = servicios.AddOpenTelemetry().ConfigureResource(r => r.AddService("murcia-datos-api"));

        otel.WithMetrics(metricas =>
        {
            metricas.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddRuntimeInstrumentation().AddMeter(MetricasDeApi.NombreDelMedidor);
            if (exportar)
            {
                metricas.AddOtlpExporter();
            }
        });

        otel.WithTracing(trazas =>
        {
            trazas.AddAspNetCoreInstrumentation(opciones =>
                    // Las sondas de salud y los ficheros del explorador no aportan nada a una traza.
                    opciones.Filter = contexto => !contexto.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase)
                        && !contexto.Request.Path.StartsWithSegments("/_framework", StringComparison.OrdinalIgnoreCase))
                .AddHttpClientInstrumentation();
            if (exportar)
            {
                trazas.AddOtlpExporter();
            }
        });

        return servicios;
    }
}

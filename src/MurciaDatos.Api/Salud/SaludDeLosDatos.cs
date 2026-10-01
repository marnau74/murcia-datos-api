using Microsoft.Extensions.Diagnostics.HealthChecks;

using MurciaDatos.Datos.Actualizacion;
using MurciaDatos.Datos.Consultas;

namespace MurciaDatos.Api.Salud;

/// <summary>
/// «Lista» cuando hay una versión de datos cargada y comprobada. Si la última comprobación contra el origen falló o
/// rechazó una versión nueva, sigue lista (sirve la anterior) pero aparece degradada, con el motivo.
/// </summary>
public sealed class SaludDeLosDatos(AlmacenDeInstantaneas almacen, ServicioDeActualizacion actualizacion) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var actual = almacen.Actual;
        if (actual is null)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                actualizacion.UltimoProblema ?? "Todavía no se han cargado datos.",
                data: Datos(actualizacion, null)));
        }

        var datos = Datos(actualizacion, actual);
        return Task.FromResult(actualizacion.UltimoProblema is { } problema
            ? HealthCheckResult.Degraded($"Se sirven los datos {actual.Etiqueta}, pero la última actualización no salió bien: {problema}", data: datos)
            : HealthCheckResult.Healthy($"Datos {actual.Etiqueta}.", datos));
    }

    private static Dictionary<string, object> Datos(ServicioDeActualizacion actualizacion, InstantaneaDatos? actual)
    {
        var datos = new Dictionary<string, object>();
        if (actual is not null)
        {
            datos["version_datos"] = actual.Etiqueta;
            datos["huella"] = actual.Huella;
            datos["cargada_en"] = actual.CargadaEn;
        }

        if (actualizacion.UltimaComprobacion is { } comprobacion)
        {
            datos["ultima_comprobacion"] = comprobacion;
        }

        return datos;
    }
}

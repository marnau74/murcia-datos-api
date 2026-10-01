using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

using MurciaDatos.Datos.Actualizacion;
using MurciaDatos.Datos.Consultas;
using MurciaDatos.Datos.Origen;

namespace MurciaDatos.Api.Salud;

/// <summary>
/// «Lista» cuando hay una versión de datos cargada y comprobada. Si la última comprobación contra el origen falló o
/// rechazó una versión nueva, sigue lista (sirve la anterior) pero aparece degradada, con el motivo. También aparece
/// degradada si hace demasiado que no se comprueba el origen: el actualizador se ha parado o no consigue terminar.
/// </summary>
public sealed class SaludDeLosDatos(
    AlmacenDeInstantaneas almacen,
    ServicioDeActualizacion actualizacion,
    IOptions<OpcionesDeDatos> opciones,
    TimeProvider reloj) : IHealthCheck
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

        if (actualizacion.UltimoProblema is { } problema)
        {
            return Task.FromResult(HealthCheckResult.Degraded($"Se sirven los datos {actual.Etiqueta}, pero la última actualización no salió bien: {problema}", data: datos));
        }

        // Dos intervalos sin comprobar el origen: algo ha parado las actualizaciones aunque no haya dejado ningún error.
        var limite = TimeSpan.FromHours(opciones.Value.IntervaloHoras * 2);
        if (actualizacion.UltimaComprobacion is { } comprobacion && reloj.GetUtcNow() - comprobacion > limite)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                $"Se sirven los datos {actual.Etiqueta}, pero el origen no se comprueba desde {comprobacion:yyyy-MM-dd HH:mm} UTC.",
                data: datos));
        }

        return Task.FromResult(HealthCheckResult.Healthy($"Datos {actual.Etiqueta}.", datos));
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

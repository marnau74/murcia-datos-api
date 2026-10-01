using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Options;

using MurciaDatos.Api.Cache;
using MurciaDatos.Datos.Actualizacion;
using MurciaDatos.Datos.Consultas;
using MurciaDatos.Datos.Origen;

namespace MurciaDatos.Api;

/// <summary>
/// Tarea en segundo plano: al arrancar carga lo que haya en disco (para servir enseguida) y luego comprueba el
/// origen cada <see cref="OpcionesDeDatos.IntervaloHoras"/> horas. Mientras no haya ninguna versión cargada
/// reintenta cada minuto. Un fallo, del tipo que sea, solo cuenta para esa vuelta: nunca para el bucle.
/// </summary>
public sealed partial class ActualizadorDeDatos(
    ServicioDeActualizacion servicio,
    AlmacenDeInstantaneas almacen,
    IOptions<OpcionesDeDatos> opciones,
    TimeProvider reloj,
    ILogger<ActualizadorDeDatos> registro) : BackgroundService
{
    private static readonly TimeSpan EsperaSinDatos = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await VueltaAsync(() => servicio.CargarDeDiscoAsync(stoppingToken), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                await VueltaAsync(() => servicio.ActualizarAsync(stoppingToken), stoppingToken);

                var espera = almacen.Actual is null ? EsperaSinDatos : TimeSpan.FromHours(opciones.Value.IntervaloHoras);
                using var temporizador = new PeriodicTimer(espera, reloj);
                await temporizador.WaitForNextTickAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Parada normal del servicio.
        }
    }

    private async Task VueltaAsync(Func<Task> accion, CancellationToken stoppingToken)
    {
        try
        {
            await accion();
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            // Cualquier fallo de una vuelta se anota y se reintenta en la siguiente: el bucle no se para nunca.
            VueltaFallida(registro, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Una vuelta del actualizador de datos ha fallado; se reintentará en la siguiente.")]
    private static partial void VueltaFallida(ILogger logger, Exception excepcion);
}

/// <summary>Cuando cambian los datos, vacía la caché de salida.</summary>
public sealed class VaciadoDeCacheAlCambiarLosDatos(IOutputCacheStore cache) : IObservadorDeDatos
{
    public async Task AlCambiarLosDatosAsync(InstantaneaDatos nueva, CancellationToken cancelacion) =>
        await cache.EvictByTagAsync(PoliticaDeCacheDeDatos.Etiqueta, cancelacion);
}

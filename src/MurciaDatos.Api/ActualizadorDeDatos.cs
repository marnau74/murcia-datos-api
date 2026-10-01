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
/// reintenta cada minuto.
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
            await servicio.CargarDeDiscoAsync(stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                await servicio.ActualizarAsync(stoppingToken);

                var espera = almacen.Actual is null ? EsperaSinDatos : TimeSpan.FromHours(opciones.Value.IntervaloHoras);
                using var temporizador = new PeriodicTimer(espera, reloj);
                await temporizador.WaitForNextTickAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Parada normal del servicio.
        }
        catch (Exception ex)
        {
            // Un fallo inesperado no debe tumbar la API: se anota y se deja de actualizar.
            Detenido(registro, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Critical, Message = "El actualizador de datos se ha detenido por un error inesperado.")]
    private static partial void Detenido(ILogger logger, Exception excepcion);
}

/// <summary>Cuando cambian los datos, vacía la caché de salida.</summary>
public sealed class VaciadoDeCacheAlCambiarLosDatos(IOutputCacheStore cache) : IObservadorDeDatos
{
    public async Task AlCambiarLosDatosAsync(InstantaneaDatos nueva, CancellationToken cancelacion) =>
        await cache.EvictByTagAsync(PoliticaDeCacheDeDatos.Etiqueta, cancelacion);
}

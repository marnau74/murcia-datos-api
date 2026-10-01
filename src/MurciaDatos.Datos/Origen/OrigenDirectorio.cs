using Microsoft.Extensions.Options;

using MurciaDatos.Datos.Contrato;

namespace MurciaDatos.Datos.Origen;

/// <summary>
/// Una carpeta local con los ficheros de una release (por ejemplo la carpeta <c>release/</c> que genera el
/// proyecto de datos). Sirve para desarrollar y para las pruebas sin depender de la red.
/// </summary>
public sealed class OrigenDirectorio(IOptions<OpcionesDeDatos> opciones) : IOrigenDeDatos
{
    public async Task<PaqueteRemoto?> ObtenerUltimoAsync(CancellationToken cancelacion)
    {
        var carpeta = opciones.Value.Ruta;
        if (string.IsNullOrWhiteSpace(carpeta) || !Directory.Exists(carpeta))
        {
            return null;
        }

        if (FicherosDelPaquete.Todos.Any(f => !File.Exists(Path.Combine(carpeta, f))))
        {
            return null;
        }

        var contrato = ContratoDatos.Leer(await File.ReadAllBytesAsync(Path.Combine(carpeta, FicherosDelPaquete.Contrato), cancelacion));

        return new PaqueteRemoto(
            $"datos-{contrato.Periodo.Hasta}",
            (fichero, _) =>
            {
                if (!FicherosDelPaquete.Todos.Contains(fichero))
                {
                    throw new ArgumentException($"Fichero no permitido: {fichero}", nameof(fichero));
                }

                return Task.FromResult<Stream>(new FileStream(Path.Combine(carpeta, fichero), FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true));
            });
    }
}

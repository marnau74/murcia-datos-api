using System.Security.Cryptography;
using System.Text;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using MurciaDatos.Datos.Consultas;
using MurciaDatos.Datos.Contrato;
using MurciaDatos.Datos.Origen;

namespace MurciaDatos.Datos.Actualizacion;

public enum ResultadoDeActualizacion
{
    /// <summary>Ya se sirve la última versión publicada.</summary>
    SinNovedades,

    /// <summary>Se ha cargado y activado una versión nueva.</summary>
    Actualizado,

    /// <summary>No hay ninguna release de datos publicada (o no se puede leer el origen).</summary>
    SinOrigen,

    /// <summary>La versión publicada no cumple el contrato o los controles: se sigue con la anterior.</summary>
    Rechazado,

    /// <summary>Un fallo técnico (red, disco…): se sigue con la anterior y se reintenta en el próximo ciclo.</summary>
    Fallido,
}

/// <summary>Quien necesite enterarse de que los datos han cambiado (por ejemplo, para vaciar la caché).</summary>
public interface IObservadorDeDatos
{
    Task AlCambiarLosDatosAsync(InstantaneaDatos nueva, CancellationToken cancelacion);
}

/// <summary>
/// Mantiene al día la instantánea activa. Consulta la última release, descarga lo que haya cambiado, verifica
/// las sumas SHA-256 y el contrato, y solo entonces cambia de versión. Si algo falla, no toca lo que se sirve.
/// </summary>
public sealed partial class ServicioDeActualizacion(
    IOrigenDeDatos origen,
    AlmacenDeInstantaneas almacen,
    IEnumerable<IObservadorDeDatos> observadores,
    IOptions<OpcionesDeDatos> opciones,
    TimeProvider reloj,
    ILogger<ServicioDeActualizacion> registro) : IDisposable
{
    private const long MaxFicheroPequenoBytes = 1024 * 1024;
    private readonly SemaphoreSlim _uno = new(1, 1);

    /// <summary>Cuándo se hizo la última comprobación contra el origen.</summary>
    public DateTimeOffset? UltimaComprobacion { get; private set; }

    public ResultadoDeActualizacion? UltimoResultado { get; private set; }

    /// <summary>Motivo del último rechazo o fallo; null si la última comprobación fue bien.</summary>
    public string? UltimoProblema { get; private set; }

    public void Dispose() => _uno.Dispose();

    private string Directorio => opciones.Value.Directorio
        ?? Path.Combine(Path.GetTempPath(), "murcia-datos-api");

    /// <summary>
    /// Al arrancar: carga la versión más reciente que ya esté en disco, para servir datos enseguida (y aunque el
    /// origen no responda) mientras se comprueba si hay una nueva.
    /// </summary>
    public async Task CargarDeDiscoAsync(CancellationToken cancelacion)
    {
        if (!Directory.Exists(Directorio))
        {
            return;
        }

        var carpetas = Directory.EnumerateDirectories(Directorio, "datos-*").OrderByDescending(c => c, StringComparer.Ordinal);
        foreach (var carpeta in carpetas)
        {
            try
            {
                var instantanea = await AbrirCarpetaAsync(carpeta, cancelacion);
                almacen.Reemplazar(instantanea);
                Cargada(registro, instantanea.Etiqueta, instantanea.Huella);
                return;
            }
            catch (Exception ex) when (ex is ContratoInvalidoException or IOException or UnauthorizedAccessException)
            {
                DescartadaDeDisco(registro, carpeta, ex.Message);
            }
        }
    }

    public async Task<ResultadoDeActualizacion> ActualizarAsync(CancellationToken cancelacion)
    {
        await _uno.WaitAsync(cancelacion);
        try
        {
            UltimaComprobacion = reloj.GetUtcNow();
            var resultado = await ActualizarSinCerrojoAsync(cancelacion);
            UltimoResultado = resultado;
            if (resultado is ResultadoDeActualizacion.SinNovedades or ResultadoDeActualizacion.Actualizado)
            {
                UltimoProblema = null;
            }

            return resultado;
        }
        finally
        {
            _uno.Release();
        }
    }

    private async Task<ResultadoDeActualizacion> ActualizarSinCerrojoAsync(CancellationToken cancelacion)
    {
        string? carpetaTemporal = null;
        try
        {
            var paquete = await origen.ObtenerUltimoAsync(cancelacion);
            if (paquete is null)
            {
                UltimoProblema = "No hay ninguna release de datos publicada.";
                return ResultadoDeActualizacion.SinOrigen;
            }

            var contratoBytes = await LeerPequenoAsync(paquete, FicherosDelPaquete.Contrato, cancelacion);
            var sumasBytes = await LeerPequenoAsync(paquete, FicherosDelPaquete.Sumas, cancelacion);
            var sumas = SumasDeVerificacion.Leer(Encoding.UTF8.GetString(sumasBytes));

            if (SumasDeVerificacion.Calcular(contratoBytes) != sumas.De(FicherosDelPaquete.Contrato))
            {
                throw new ContratoInvalidoException("contrato.json no coincide con la suma publicada.");
            }

            var contrato = ContratoDatos.Leer(contratoBytes);
            var sumaBase = sumas.De(FicherosDelPaquete.BaseDeDatos);
            var huella = sumaBase[..12];

            var actual = almacen.Actual;
            if (actual is not null
                && (string.CompareOrdinal(paquete.Etiqueta, actual.Etiqueta) < 0
                    || (paquete.Etiqueta == actual.Etiqueta && huella == actual.Huella)))
            {
                return ResultadoDeActualizacion.SinNovedades;
            }

            var destino = Path.Combine(Directorio, $"{paquete.Etiqueta}-{huella}");
            carpetaTemporal = Path.Combine(Directorio, $"descarga-{Guid.NewGuid():N}");
            Directory.CreateDirectory(carpetaTemporal);

            var problemas = ValidadorDeContrato.Validar(contrato);
            if (problemas.Count > 0)
            {
                throw new ContratoInvalidoException(string.Join(" ", problemas));
            }

            await Descargar(paquete, FicherosDelPaquete.BaseDeDatos, Path.Combine(carpetaTemporal, FicherosDelPaquete.BaseDeDatos), sumaBase, cancelacion);
            await File.WriteAllBytesAsync(Path.Combine(carpetaTemporal, FicherosDelPaquete.Contrato), contratoBytes, cancelacion);
            await File.WriteAllBytesAsync(Path.Combine(carpetaTemporal, FicherosDelPaquete.Sumas), sumasBytes, cancelacion);

            // Se prueba la versión ANTES de moverla a su sitio definitivo: si no vale, no deja rastro.
            using (CargadorDeInstantanea.Abrir(Path.Combine(carpetaTemporal, FicherosDelPaquete.BaseDeDatos), null, paquete.Etiqueta, huella, contrato, sumasVerificadas: true, reloj))
            {
            }

            if (Directory.Exists(destino))
            {
                Directory.Delete(destino, recursive: true);
            }

            Directory.Move(carpetaTemporal, destino);
            carpetaTemporal = null;

            var instantanea = CargadorDeInstantanea.Abrir(Path.Combine(destino, FicherosDelPaquete.BaseDeDatos), null, paquete.Etiqueta, huella, contrato, sumasVerificadas: true, reloj);
            almacen.Reemplazar(instantanea);
            Cargada(registro, instantanea.Etiqueta, instantanea.Huella);

            foreach (var observador in observadores)
            {
                await observador.AlCambiarLosDatosAsync(instantanea, cancelacion);
            }

            LimpiarVersionesAntiguas(destino);
            return ResultadoDeActualizacion.Actualizado;
        }
        catch (ContratoInvalidoException ex)
        {
            UltimoProblema = ex.Message;
            Rechazada(registro, ex.Message);
            return ResultadoDeActualizacion.Rechazado;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or InvalidDataException or UnauthorizedAccessException && !cancelacion.IsCancellationRequested)
        {
            UltimoProblema = ex.Message;
            Fallida(registro, ex);
            return ResultadoDeActualizacion.Fallido;
        }
        finally
        {
            if (carpetaTemporal is not null)
            {
                try
                {
                    Directory.Delete(carpetaTemporal, recursive: true);
                }
                catch (IOException)
                {
                }
            }
        }
    }

    private async Task<InstantaneaDatos> AbrirCarpetaAsync(string carpeta, CancellationToken cancelacion)
    {
        var nombre = Path.GetFileName(carpeta);
        var guion = nombre.LastIndexOf('-');
        if (guion <= 0)
        {
            throw new ContratoInvalidoException($"La carpeta {nombre} no tiene el formato etiqueta-huella.");
        }

        var contratoBytes = await File.ReadAllBytesAsync(Path.Combine(carpeta, FicherosDelPaquete.Contrato), cancelacion);
        var sumas = SumasDeVerificacion.Leer(await File.ReadAllTextAsync(Path.Combine(carpeta, FicherosDelPaquete.Sumas), cancelacion));
        var ruta = Path.Combine(carpeta, FicherosDelPaquete.BaseDeDatos);

        string suma;
        await using (var fichero = File.OpenRead(ruta))
        {
            suma = Convert.ToHexStringLower(await SHA256.HashDataAsync(fichero, cancelacion));
        }

        if (suma != sumas.De(FicherosDelPaquete.BaseDeDatos) || SumasDeVerificacion.Calcular(contratoBytes) != sumas.De(FicherosDelPaquete.Contrato))
        {
            throw new ContratoInvalidoException("Los ficheros guardados en disco no coinciden con sus sumas.");
        }

        return CargadorDeInstantanea.Abrir(ruta, null, nombre[..guion], nombre[(guion + 1)..], ContratoDatos.Leer(contratoBytes), sumasVerificadas: true, reloj);
    }

    private static async Task<byte[]> LeerPequenoAsync(PaqueteRemoto paquete, string fichero, CancellationToken cancelacion)
    {
        await using var flujo = await paquete.Abrir(fichero, cancelacion);
        using var memoria = new MemoryStream();
        var buffer = new byte[8192];
        int leidos;
        while ((leidos = await flujo.ReadAsync(buffer, cancelacion)) > 0)
        {
            memoria.Write(buffer, 0, leidos);
            if (memoria.Length > MaxFicheroPequenoBytes)
            {
                throw new ContratoInvalidoException($"{fichero} es demasiado grande.");
            }
        }

        return memoria.ToArray();
    }

    private async Task Descargar(PaqueteRemoto paquete, string fichero, string destino, string sumaEsperada, CancellationToken cancelacion)
    {
        var maximo = (long)opciones.Value.MaxDescargaMb * 1024 * 1024;
        using var suma = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        await using (var origenFlujo = await paquete.Abrir(fichero, cancelacion))
        await using (var salida = new FileStream(destino, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            var buffer = new byte[81920];
            long total = 0;
            int leidos;
            while ((leidos = await origenFlujo.ReadAsync(buffer, cancelacion)) > 0)
            {
                total += leidos;
                if (total > maximo)
                {
                    throw new ContratoInvalidoException($"{fichero} supera el tamaño máximo de {opciones.Value.MaxDescargaMb} MB.");
                }

                suma.AppendData(buffer, 0, leidos);
                await salida.WriteAsync(buffer.AsMemory(0, leidos), cancelacion);
            }
        }

        if (Convert.ToHexStringLower(suma.GetHashAndReset()) != sumaEsperada)
        {
            throw new ContratoInvalidoException($"La suma SHA-256 de {fichero} no coincide con la publicada.");
        }
    }

    /// <summary>Se conservan la versión nueva y la anterior; el resto sobra.</summary>
    private void LimpiarVersionesAntiguas(string activa)
    {
        var carpetas = Directory.EnumerateDirectories(Directorio, "datos-*")
            .Where(c => !string.Equals(c, activa, StringComparison.Ordinal))
            .OrderByDescending(c => c, StringComparer.Ordinal)
            .Skip(1);

        foreach (var carpeta in carpetas)
        {
            try
            {
                Directory.Delete(carpeta, recursive: true);
            }
            catch (IOException)
            {
                // En uso (Windows): se borrará en la próxima actualización.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Datos {Etiqueta} ({Huella}) cargados y en servicio.")]
    private static partial void Cargada(ILogger logger, string etiqueta, string huella);

    [LoggerMessage(Level = LogLevel.Error, Message = "Versión de datos rechazada, se sigue con la anterior: {Motivo}")]
    private static partial void Rechazada(ILogger logger, string motivo);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No se ha podido actualizar los datos; se reintentará en el próximo ciclo.")]
    private static partial void Fallida(ILogger logger, Exception excepcion);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Se descarta la versión guardada en {Carpeta}: {Motivo}")]
    private static partial void DescartadaDeDisco(ILogger logger, string carpeta, string motivo);
}

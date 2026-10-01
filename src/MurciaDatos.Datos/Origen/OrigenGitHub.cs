using System.Text.Json;
using System.Text.RegularExpressions;

using Microsoft.Extensions.Options;

namespace MurciaDatos.Datos.Origen;

/// <summary>
/// Las releases <c>datos-AAAA-MM</c> del repositorio configurado. Solo se descarga de ese repositorio: las
/// direcciones de los ficheros se construyen aquí, no se toman de lo que devuelva la API de GitHub.
/// </summary>
public sealed partial class OrigenGitHub(HttpClient http, IOptions<OpcionesDeDatos> opciones) : IOrigenDeDatos
{
    public const string NombreCliente = "origen-datos";

    [GeneratedRegex(@"^datos-\d{4}-(0[1-9]|1[0-2])$")]
    private static partial Regex EtiquetaDeDatos();

    private readonly string _repositorio = opciones.Value.Repositorio;

    public async Task<PaqueteRemoto?> ObtenerUltimoAsync(CancellationToken cancelacion)
    {
        using var peticion = new HttpRequestMessage(HttpMethod.Get, new Uri($"https://api.github.com/repos/{_repositorio}/releases?per_page=30"));
        peticion.Headers.Accept.ParseAdd("application/vnd.github+json");
        peticion.Headers.UserAgent.ParseAdd("murcia-datos-api");

        using var respuesta = await http.SendAsync(peticion, cancelacion);
        respuesta.EnsureSuccessStatusCode();

        using var documento = await JsonDocument.ParseAsync(await respuesta.Content.ReadAsStreamAsync(cancelacion), cancellationToken: cancelacion);

        string? etiqueta = null;
        foreach (var release in documento.RootElement.EnumerateArray())
        {
            if (Booleano(release, "draft") || Booleano(release, "prerelease"))
            {
                continue;
            }

            var candidata = release.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
            if (candidata is null || !EtiquetaDeDatos().IsMatch(candidata) || !TieneLosFicheros(release))
            {
                continue;
            }

            // AAAA-MM de ancho fijo: el orden de texto es el orden cronológico.
            if (etiqueta is null || string.CompareOrdinal(candidata, etiqueta) > 0)
            {
                etiqueta = candidata;
            }
        }

        if (etiqueta is null)
        {
            return null;
        }

        return new PaqueteRemoto(etiqueta, (fichero, ct) => AbrirAsync(etiqueta, fichero, ct));
    }

    private static bool Booleano(JsonElement elemento, string propiedad) =>
        elemento.TryGetProperty(propiedad, out var valor) && valor.ValueKind == JsonValueKind.True;

    private static bool TieneLosFicheros(JsonElement release)
    {
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var nombres = assets.EnumerateArray()
            .Select(a => a.TryGetProperty("name", out var n) ? n.GetString() : null)
            .ToHashSet(StringComparer.Ordinal);

        return FicherosDelPaquete.Todos.All(nombres.Contains);
    }

    private async Task<Stream> AbrirAsync(string etiqueta, string fichero, CancellationToken cancelacion)
    {
        if (!FicherosDelPaquete.Todos.Contains(fichero))
        {
            throw new ArgumentException($"Fichero no permitido: {fichero}", nameof(fichero));
        }

        var direccion = new Uri($"https://github.com/{_repositorio}/releases/download/{etiqueta}/{fichero}");
        var respuesta = await http.GetAsync(direccion, HttpCompletionOption.ResponseHeadersRead, cancelacion);

        try
        {
            respuesta.EnsureSuccessStatusCode();
            return new FlujoQueLiberaLaRespuesta(await respuesta.Content.ReadAsStreamAsync(cancelacion), respuesta);
        }
        catch
        {
            respuesta.Dispose();
            throw;
        }
    }

    /// <summary>Al cerrar el flujo se libera también la respuesta HTTP que lo originó.</summary>
    private sealed class FlujoQueLiberaLaRespuesta(Stream interno, HttpResponseMessage respuesta) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => interno.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => interno.ReadAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                interno.Dispose();
                respuesta.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}

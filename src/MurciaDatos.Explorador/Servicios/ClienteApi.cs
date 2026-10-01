using System.Net.Http.Json;
using System.Text.Json;

namespace MurciaDatos.Explorador.Servicios;

/// <summary>Las llamadas del explorador a la API (la misma que cualquier otro cliente).</summary>
public interface IClienteApi
{
    Task<Catalogos> CargarCatalogosAsync(CancellationToken cancelacion = default);

    /// <summary>Pide una serie; <paramref name="consulta"/> es la cadena de parámetros sin el «?».</summary>
    Task<RespuestaSerieApi> CargarSerieAsync(string recurso, string consulta, CancellationToken cancelacion = default);
}

public sealed class ClienteApi(HttpClient http) : IClienteApi
{
    public async Task<Catalogos> CargarCatalogosAsync(CancellationToken cancelacion = default)
    {
        var metadatos = Leer<MetadatosApi>("v1/metadatos", cancelacion);
        var territorios = Leer<Listado<TerritorioApi>>("v1/territorios", cancelacion);
        var tipos = Leer<Listado<OpcionApi>>("v1/tipos-alojamiento", cancelacion);
        var residencias = Leer<Listado<OpcionApi>>("v1/residencias", cancelacion);
        var medidas = Leer<Listado<MedidaApi>>("v1/medidas", cancelacion);

        await Task.WhenAll(metadatos, territorios, tipos, residencias, medidas);
        return new Catalogos(metadatos.Result, territorios.Result.Datos, tipos.Result.Datos, residencias.Result.Datos, medidas.Result.Datos);
    }

    public Task<RespuestaSerieApi> CargarSerieAsync(string recurso, string consulta, CancellationToken cancelacion = default) =>
        Leer<RespuestaSerieApi>($"v1/{recurso}?{consulta}", cancelacion);

    private async Task<T> Leer<T>(string ruta, CancellationToken cancelacion)
    {
        using var respuesta = await http.GetAsync(ruta, cancelacion);
        if (!respuesta.IsSuccessStatusCode)
        {
            throw await ErrorAsync(respuesta, cancelacion);
        }

        return await respuesta.Content.ReadFromJsonAsync<T>(cancelacion) ?? throw new ErrorDeApiException("Respuesta vacía", "La API ha devuelto una respuesta vacía.", null);
    }

    private static async Task<ErrorDeApiException> ErrorAsync(HttpResponseMessage respuesta, CancellationToken cancelacion)
    {
        try
        {
            using var documento = await JsonDocument.ParseAsync(await respuesta.Content.ReadAsStreamAsync(cancelacion), cancellationToken: cancelacion);
            var raiz = documento.RootElement;
            var titulo = raiz.TryGetProperty("title", out var t) ? t.GetString() : null;
            var detalle = raiz.TryGetProperty("detail", out var d) ? d.GetString() : null;

            var errores = new Dictionary<string, string[]>();
            if (raiz.TryGetProperty("errors", out var e) && e.ValueKind == JsonValueKind.Object)
            {
                foreach (var parametro in e.EnumerateObject())
                {
                    errores[parametro.Name] = [.. parametro.Value.EnumerateArray().Select(m => m.GetString() ?? string.Empty)];
                }
            }

            if (respuesta.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                detalle = $"{detalle} Es el límite de peticiones por IP de la API pública.";
            }

            return new ErrorDeApiException(titulo ?? $"Error {(int)respuesta.StatusCode}", detalle, errores);
        }
        catch (JsonException)
        {
            return new ErrorDeApiException($"Error {(int)respuesta.StatusCode}", "La API ha respondido con un error.", null);
        }
    }
}

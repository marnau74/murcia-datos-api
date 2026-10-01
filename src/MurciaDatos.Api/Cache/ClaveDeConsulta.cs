using System.Security.Cryptography;
using System.Text;

using Microsoft.AspNetCore.OutputCaching;

using MurciaDatos.Api.Filtros;
using MurciaDatos.Datos.Consultas;

namespace MurciaDatos.Api.Cache;

/// <summary>
/// La forma normalizada de una petición: la misma consulta escrita de otra manera (otro orden de parámetros,
/// mayúsculas, listas desordenadas) tiene la misma clave y, por tanto, la misma entrada de caché y el mismo ETag.
/// </summary>
public static class ClaveDeConsulta
{
    private static readonly HashSet<string> ListasSinOrden = new(["territorio", "tipo", "residencia"], StringComparer.Ordinal);

    public static string De(HttpRequest peticion)
    {
        var partes = new List<string>();
        foreach (var (clave, valores) in peticion.Query.OrderBy(q => q.Key, StringComparer.OrdinalIgnoreCase))
        {
            var nombre = clave.ToLowerInvariant();
            var texto = string.Join(",", valores.Select(v => v ?? string.Empty));
            var normalizado = nombre == "formato" || nombre == "excel"
                ? texto.Trim().ToLowerInvariant()
                : string.Join(",", Elementos(nombre, texto));
            partes.Add($"{nombre}={normalizado}");
        }

        var formatoPedido = peticion.Query.TryGetValue("formato", out var f) ? f.ToString().Trim().ToLowerInvariant() : string.Empty;
        var formato = formatoPedido.Length > 0 ? formatoPedido : ValidadorDeConsultas.PideCsv(peticion.Headers.Accept.ToString()) ? "csv" : "json";

        return $"{peticion.Path.Value?.ToLowerInvariant()}?{string.Join("&", partes)}|{formato}";
    }

    private static IEnumerable<string> Elementos(string nombre, string texto)
    {
        var elementos = texto.Split(',', StringSplitOptions.TrimEntries).Select(e => e.ToLowerInvariant());
        return ListasSinOrden.Contains(nombre)
            ? elementos.Where(e => e.Length > 0).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
            : elementos;
    }

    /// <summary>ETag débil de una respuesta: depende de la versión de los datos y de la consulta normalizada.</summary>
    public static string ETag(string versionDeDatos, string clave) =>
        "W/\"" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(versionDeDatos + "|" + clave)))[..24] + "\"";

    /// <summary>¿Alguna de las etiquetas de <c>If-None-Match</c> coincide? (también acepta <c>*</c>).</summary>
    public static bool Coincide(string? ifNoneMatch, string etag)
    {
        if (string.IsNullOrWhiteSpace(ifNoneMatch))
        {
            return false;
        }

        static string SinDebil(string e) => e.StartsWith("W/", StringComparison.Ordinal) ? e[2..] : e;

        return ifNoneMatch.Split(',', StringSplitOptions.TrimEntries)
            .Any(e => e == "*" || SinDebil(e) == SinDebil(etag));
    }
}

/// <summary>Caché de salida de los datos: una hora, variando por la consulta normalizada y etiquetada para vaciarla de golpe.</summary>
public sealed class PoliticaDeCacheDeDatos : IOutputCachePolicy
{
    public const string Nombre = "datos";
    public const string Etiqueta = "datos";

    public ValueTask CacheRequestAsync(OutputCacheContext context, CancellationToken cancellation)
    {
        var peticion = context.HttpContext.Request;
        var esLectura = HttpMethods.IsGet(peticion.Method) || HttpMethods.IsHead(peticion.Method);

        context.EnableOutputCaching = esLectura;
        context.AllowCacheLookup = esLectura;
        context.AllowCacheStorage = esLectura;
        context.AllowLocking = true;
        context.ResponseExpirationTimeSpan = TimeSpan.FromHours(1);
        context.CacheVaryByRules.VaryByValues["consulta"] = ClaveDeConsulta.De(peticion);

        // Aunque no se vaciase la caché al cambiar los datos, una versión nueva nunca sirve respuestas de la vieja.
        var almacen = context.HttpContext.RequestServices.GetRequiredService<AlmacenDeInstantaneas>();
        context.CacheVaryByRules.VaryByValues["version"] = almacen.Actual?.VersionDeCache ?? "sin-datos";
        context.Tags.Add(Etiqueta);

        return ValueTask.CompletedTask;
    }

    public ValueTask ServeFromCacheAsync(OutputCacheContext context, CancellationToken cancellation) => ValueTask.CompletedTask;

    public ValueTask ServeResponseAsync(OutputCacheContext context, CancellationToken cancellation)
    {
        // Solo se guardan las respuestas correctas: un error (400, 503…) no debe quedarse en caché.
        if (context.HttpContext.Response.StatusCode != StatusCodes.Status200OK)
        {
            context.AllowCacheStorage = false;
        }

        return ValueTask.CompletedTask;
    }
}

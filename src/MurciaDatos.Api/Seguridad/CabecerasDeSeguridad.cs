using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace MurciaDatos.Api.Seguridad;

/// <summary>
/// La política de contenido (CSP) del explorador: solo recursos propios y WebAssembly. Blazor escribe en la página
/// un <c>importmap</c> en línea con los nombres con huella de sus ficheros; como cambia en cada compilación, su
/// hash SHA-256 se calcula leyendo la propia página y se añade a la política: así no hace falta <c>'unsafe-inline'</c>.
/// </summary>
public sealed partial class PoliticaDeContenido(IWebHostEnvironment entorno)
{
    private const string Antes = "default-src 'self'; script-src 'self' 'wasm-unsafe-eval'";

    private const string Despues =
        "; style-src 'self'; img-src 'self' data:; font-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";

    private readonly Lazy<string> _valor = new(() => Calcular(entorno), LazyThreadSafetyMode.ExecutionAndPublication);

    public string Valor => _valor.Value;

    [GeneratedRegex(@"<script type=""importmap"">(?<contenido>.*?)</script>", RegexOptions.Singleline)]
    private static partial Regex ImportMap();

    internal static string Calcular(IWebHostEnvironment entorno)
    {
        var hashes = new StringBuilder();
        var pagina = entorno.WebRootFileProvider.GetFileInfo("index.html");
        if (pagina.Exists)
        {
            using var lector = new StreamReader(pagina.CreateReadStream(), Encoding.UTF8);
            foreach (Match coincidencia in ImportMap().Matches(lector.ReadToEnd()))
            {
                // El navegador normaliza los saltos de línea del script (CRLF y CR pasan a LF) antes de calcular el hash.
                var contenido = coincidencia.Groups["contenido"].Value.ReplaceLineEndings("\n");
                if (contenido.Length > 0)
                {
                    hashes.Append(" 'sha256-").Append(Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(contenido)))).Append('\'');
                }
            }
        }

        return Antes + hashes + Despues;
    }
}

/// <summary>
/// Cabeceras de seguridad de todas las respuestas. La página de documentación de la API (Scalar), que usa scripts
/// y estilos en línea, queda fuera de la política de contenido estricta del explorador.
/// </summary>
public sealed class CabecerasDeSeguridad(RequestDelegate siguiente, PoliticaDeContenido politica)
{
    public Task InvokeAsync(HttpContext contexto)
    {
        contexto.Response.OnStarting(() =>
        {
            var cabeceras = contexto.Response.Headers;
            cabeceras.XContentTypeOptions = "nosniff";
            cabeceras["Referrer-Policy"] = "no-referrer";
            cabeceras["Cross-Origin-Opener-Policy"] = "same-origin";

            if (!contexto.Request.Path.StartsWithSegments("/scalar", StringComparison.OrdinalIgnoreCase))
            {
                cabeceras.ContentSecurityPolicy = politica.Valor;
                cabeceras.XFrameOptions = "DENY";
            }

            return Task.CompletedTask;
        });

        return siguiente(contexto);
    }
}

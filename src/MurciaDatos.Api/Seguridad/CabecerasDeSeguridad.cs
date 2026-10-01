namespace MurciaDatos.Api.Seguridad;

/// <summary>
/// Cabeceras de seguridad de todas las respuestas. La política de contenido del explorador es estricta (solo
/// recursos propios; WebAssembly permitido); la página de documentación de la API, que usa scripts y estilos en
/// línea, queda fuera de ella.
/// </summary>
public sealed class CabecerasDeSeguridad(RequestDelegate siguiente)
{
    internal const string PoliticaDelExplorador =
        "default-src 'self'; script-src 'self' 'wasm-unsafe-eval'; style-src 'self'; img-src 'self' data:; font-src 'self'; connect-src 'self'; " +
        "object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";

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
                cabeceras.ContentSecurityPolicy = PoliticaDelExplorador;
                cabeceras.XFrameOptions = "DENY";
            }

            return Task.CompletedTask;
        });

        return siguiente(contexto);
    }
}

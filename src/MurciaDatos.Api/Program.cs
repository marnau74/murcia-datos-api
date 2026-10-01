using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

using MurciaDatos.Api;
using MurciaDatos.Api.Cache;
using MurciaDatos.Api.Endpoints;
using MurciaDatos.Api.Salud;
using MurciaDatos.Api.Seguridad;
using MurciaDatos.Datos.Actualizacion;
using MurciaDatos.Datos.Consultas;
using MurciaDatos.Datos.Origen;

using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// ---- Configuración ----------------------------------------------------------------------------------
builder.Services.AddOptions<OpcionesDeDatos>().BindConfiguration(OpcionesDeDatos.Seccion).ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<OpcionesDeLimites>().BindConfiguration(OpcionesDeLimites.Seccion).ValidateDataAnnotations().ValidateOnStart();

// ---- Datos ------------------------------------------------------------------------------------------
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<AlmacenDeInstantaneas>();
builder.Services.AddSingleton<ServicioDeActualizacion>();
builder.Services.AddSingleton<IObservadorDeDatos, VaciadoDeCacheAlCambiarLosDatos>();
builder.Services.AddSingleton<MetricasDeApi>();
builder.Services.AddSingleton<PoliticaDeContenido>();
builder.Services.AddHostedService<ActualizadorDeDatos>();

// La descarga tiene sus propios tiempos: un fichero de datos puede ser grande y la red de GitHub, lenta.
// Los reintentos solo se aplican a este cliente (el de la descarga), nunca a las consultas.
builder.Services.AddHttpClient(OrigenGitHub.NombreCliente)
    .AddStandardResilienceHandler(opciones =>
    {
        opciones.AttemptTimeout.Timeout = TimeSpan.FromMinutes(5);
        opciones.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(15);
        opciones.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(15);
    });

builder.Services.AddSingleton<IOrigenDeDatos>(servicios =>
{
    var opciones = servicios.GetRequiredService<IOptions<OpcionesDeDatos>>();
    return opciones.Value.Origen == TipoDeOrigen.Directorio
        ? new OrigenDirectorio(opciones)
        : new OrigenGitHub(servicios.GetRequiredService<IHttpClientFactory>().CreateClient(OrigenGitHub.NombreCliente), opciones);
});

builder.Services.AddHealthChecks().AddCheck<SaludDeLosDatos>("datos", tags: ["ready"]);

// ---- HTTP: JSON, errores, compresión, caché, CORS ---------------------------------------------------
builder.Services.ConfigureHttpJsonOptions(opciones =>
{
    // Tildes y eñes tal cual, no como ó: el JSON se lee mejor y pesa menos.
    opciones.SerializerOptions.Encoder = JavaScriptEncoder.Create(UnicodeRanges.BasicLatin, UnicodeRanges.Latin1Supplement, UnicodeRanges.LatinExtendedA);
    opciones.SerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never;
});

builder.Services.AddProblemDetails();
builder.Services.AddResponseCompression(opciones =>
{
    opciones.EnableForHttps = true;
    opciones.Providers.Add<BrotliCompressionProvider>();
    opciones.Providers.Add<GzipCompressionProvider>();
    opciones.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(["text/csv", "application/problem+json"]);
});
builder.Services.AddOutputCache(opciones => opciones.AddPolicy(PoliticaDeCacheDeDatos.Nombre, constructor => constructor.AddPolicy<PoliticaDeCacheDeDatos>()));

// Es una API pública de datos de solo lectura: cualquier web puede consultarla desde el navegador.
builder.Services.AddCors(opciones => opciones.AddDefaultPolicy(politica => politica
    .AllowAnyOrigin()
    .WithMethods("GET", "HEAD", "OPTIONS")
    .AllowAnyHeader()
    .WithExposedHeaders("ETag", "RateLimit-Limit", "RateLimit-Remaining", "RateLimit-Reset", "RateLimit-Policy", "Retry-After")
    .SetPreflightMaxAge(TimeSpan.FromHours(1))));

// Detrás de un proxy (Caddy, Render…) la IP real del cliente llega en X-Forwarded-For.
// (La configuración se lee al construir las opciones, no antes, para que los tests puedan cambiarla.)
builder.Services.AddOptions<ForwardedHeadersOptions>().Configure<IConfiguration>((opciones, configuracion) =>
{
    if (!configuracion.GetValue<bool>("Proxy:ConfiarEnCabecerasReenviadas"))
    {
        opciones.ForwardedHeaders = ForwardedHeaders.None;
        return;
    }

    opciones.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    opciones.KnownIPNetworks.Clear();
    opciones.KnownProxies.Clear();
    opciones.ForwardLimit = 1;
});

// ---- Documentación ----------------------------------------------------------------------------------
builder.Services.AddOpenApi("v1", opciones =>
{
    // Las series se pueden pedir también como CSV: se anota en la respuesta 200 junto al JSON.
    opciones.AddOperationTransformer((operacion, contexto, _) =>
    {
        if (contexto.Description.RelativePath?.StartsWith("v1/", StringComparison.Ordinal) == true
            && contexto.Description.RelativePath is "v1/demanda" or "v1/oferta" or "v1/precios"
            && operacion.Responses?.TryGetValue("200", out var ok) == true
            && ok.Content is not null)
        {
            ok.Content["text/csv"] = new OpenApiMediaType { Schema = new OpenApiSchema { Type = JsonSchemaType.String } };
        }

        return Task.CompletedTask;
    });

    opciones.AddDocumentTransformer((documento, _, _) =>
    {
        documento.Info = new OpenApiInfo
        {
            Title = "API de datos de turismo de Murcia",
            Version = "1.0.0",
            Description = """
            Demanda, oferta y precios del alojamiento turístico en la Región de Murcia (INE y murciaturistica.es), servidos desde la
            release mensual del proyecto murcia-open-data.

            **Convenciones**
            - Solo lectura (`GET`). Prefijo `/v1`. Identificadores en `kebab-case`; fechas como `AAAA-MM`.
            - Un dato que no existe (un mes sin publicar, una medida que no aplica) es `null`, **nunca 0**.
            - Los valores provisionales se marcan con `provisional`; `meta.provisional_desde` dice desde qué mes lo son.
            - Los errores son `application/problem+json` (RFC 9457) y enumeran los valores permitidos.
            - JSON o CSV (`formato=csv` o `Accept: text/csv`; `excel=true` para abrirlo directamente en Excel en español).
            - Caché: `ETag` y `If-None-Match` (304). Límite de peticiones por IP: cabeceras `RateLimit-*`; al pasarse, 429 con `Retry-After`.
            """.ReplaceLineEndings("\n"),
        };

        return Task.CompletedTask;
    });
});

builder.Services.ConfigurarTelemetria(builder.Configuration);

var app = builder.Build();

// ---- Tubería HTTP -----------------------------------------------------------------------------------
app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler();
    app.UseHsts();
}

app.UseStatusCodePages();
app.UseMiddleware<CabecerasDeSeguridad>();
app.UseCors();
app.UseResponseCompression();
app.UseMiddleware<LimitadorPorIp>();
app.UseOutputCache();

app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

app.MapOpenApi("/openapi/{documentName}.json");
app.MapScalarApiReference("/scalar", opciones => opciones
    .WithTitle("API de datos de turismo de Murcia")
    .WithOpenApiRoutePattern("/openapi/{documentName}.json")
    .WithDefaultHttpClient(ScalarTarget.Shell, ScalarClient.Curl));

app.MapearEndpointsDeDatos();

// Sondas de salud: viva (el proceso responde) y lista (hay datos cargados).
app.MapGet("/health/live", () => Results.Ok(new { estado = "viva" })).ExcludeFromDescription();
app.MapGet("/health/ready", async (HealthCheckService servicio, CancellationToken cancelacion) =>
{
    var informe = await servicio.CheckHealthAsync(c => c.Tags.Contains("ready"), cancelacion);
    var cuerpo = new
    {
        estado = informe.Status switch { HealthStatus.Healthy => "lista", HealthStatus.Degraded => "degradada", _ => "no_lista" },
        comprobaciones = informe.Entries.ToDictionary(e => e.Key, e => new { estado = e.Value.Status.ToString(), descripcion = e.Value.Description, datos = e.Value.Data }),
    };

    // Degradada sigue sirviendo (la versión anterior): no se saca de rotación.
    return Results.Json(cuerpo, statusCode: informe.Status == HealthStatus.Unhealthy ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status200OK);
}).ExcludeFromDescription();

app.MapFallbackToFile("index.html");

await app.RunAsync();

/// <summary>Visible para los tests de integración (<c>WebApplicationFactory&lt;Program&gt;</c>).</summary>
public partial class Program;

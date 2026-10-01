using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Net;
using System.Net.Sockets;

using Microsoft.Extensions.Options;

namespace MurciaDatos.Api.Seguridad;

/// <summary>Configuración de la sección <c>Limites</c>.</summary>
public sealed class OpcionesDeLimites
{
    public const string Seccion = "Limites";

    /// <summary>Peticiones que una misma IP puede hacer en cada ventana.</summary>
    [Range(1, 1_000_000)]
    public int PeticionesPorVentana { get; set; } = 120;

    [Range(1, 3600)]
    public int VentanaSegundos { get; set; } = 60;
}

/// <summary>
/// Limita las peticiones por IP con una ventana fija. Responde con las cabeceras <c>RateLimit-*</c> en todas las
/// respuestas y con 429 y <c>Retry-After</c> al pasarse del límite. La IP real llega ya resuelta (detrás de un
/// proxy, por las cabeceras reenviadas); las IPv6 se agrupan por /64 para que cambiar de dirección dentro de la
/// misma red no sirva para saltarse el límite.
/// </summary>
public sealed class LimitadorPorIp(RequestDelegate siguiente, IOptions<OpcionesDeLimites> opciones, TimeProvider reloj)
{
    private const int PurgarSuperando = 10_000;

    private readonly ConcurrentDictionary<string, Ventana> _ventanas = new();
    private long _ultimaPurga;

    public async Task InvokeAsync(HttpContext contexto)
    {
        if (!SeLimita(contexto.Request.Path))
        {
            await siguiente(contexto);
            return;
        }

        var limite = opciones.Value.PeticionesPorVentana;
        var duracion = TimeSpan.FromSeconds(opciones.Value.VentanaSegundos);
        var ahora = reloj.GetUtcNow();

        var ventana = _ventanas.GetOrAdd(Clave(contexto.Connection.RemoteIpAddress), _ => new Ventana());
        var (permitida, restantes, reinicio) = ventana.Contar(ahora, duracion, limite);
        Purgar(ahora, duracion);

        var segundos = Math.Max(1, (int)Math.Ceiling((reinicio - ahora).TotalSeconds));
        var cabeceras = contexto.Response.Headers;
        cabeceras["RateLimit-Limit"] = limite.ToString(CultureInfo.InvariantCulture);
        cabeceras["RateLimit-Remaining"] = restantes.ToString(CultureInfo.InvariantCulture);
        cabeceras["RateLimit-Reset"] = segundos.ToString(CultureInfo.InvariantCulture);
        cabeceras["RateLimit-Policy"] = $"{limite};w={opciones.Value.VentanaSegundos}";

        if (!permitida)
        {
            cabeceras.RetryAfter = segundos.ToString(CultureInfo.InvariantCulture);
            contexto.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            await contexto.Response.WriteAsJsonAsync(
                new Microsoft.AspNetCore.Mvc.ProblemDetails
                {
                    Status = StatusCodes.Status429TooManyRequests,
                    Title = "Demasiadas peticiones",
                    Detail = $"Has superado el límite de {limite} peticiones cada {opciones.Value.VentanaSegundos} segundos. Vuelve a intentarlo dentro de {segundos} s.",
                    Type = "https://httpstatuses.io/429",
                },
                options: null,
                contentType: "application/problem+json",
                cancellationToken: contexto.RequestAborted);
            return;
        }

        await siguiente(contexto);
    }

    /// <summary>La API y su documentación se limitan; el explorador (ficheros estáticos) y las sondas de salud, no.</summary>
    private static bool SeLimita(PathString ruta) =>
        ruta.StartsWithSegments("/v1", StringComparison.OrdinalIgnoreCase) || ruta.StartsWithSegments("/openapi", StringComparison.OrdinalIgnoreCase);

    internal static string Clave(IPAddress? direccion)
    {
        if (direccion is null)
        {
            return "desconocida";
        }

        if (direccion.IsIPv4MappedToIPv6)
        {
            direccion = direccion.MapToIPv4();
        }

        if (direccion.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = direccion.GetAddressBytes();
            return Convert.ToHexStringLower(bytes.AsSpan(0, 8)) + "::/64";
        }

        return direccion.ToString();
    }

    private void Purgar(DateTimeOffset ahora, TimeSpan duracion)
    {
        if (_ventanas.Count < PurgarSuperando)
        {
            return;
        }

        // Como mucho una purga por ventana: quita las IPs cuya ventana ya terminó.
        var anterior = Interlocked.Read(ref _ultimaPurga);
        if (ahora.UtcTicks - anterior < duracion.Ticks || Interlocked.CompareExchange(ref _ultimaPurga, ahora.UtcTicks, anterior) != anterior)
        {
            return;
        }

        foreach (var (clave, ventana) in _ventanas)
        {
            if (ventana.HaCaducado(ahora, duracion))
            {
                _ventanas.TryRemove(clave, out _);
            }
        }
    }

    private sealed class Ventana
    {
        private readonly Lock _cerrojo = new();
        private DateTimeOffset _inicio;
        private int _contador;

        public (bool Permitida, int Restantes, DateTimeOffset Reinicio) Contar(DateTimeOffset ahora, TimeSpan duracion, int limite)
        {
            lock (_cerrojo)
            {
                if (_contador == 0 || ahora - _inicio >= duracion)
                {
                    _inicio = ahora;
                    _contador = 0;
                }

                if (_contador >= limite)
                {
                    return (false, 0, _inicio + duracion);
                }

                _contador++;
                return (true, limite - _contador, _inicio + duracion);
            }
        }

        public bool HaCaducado(DateTimeOffset ahora, TimeSpan duracion)
        {
            lock (_cerrojo)
            {
                return ahora - _inicio >= duracion;
            }
        }
    }
}

using System.Text.Json;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

using MurciaDatos.Datos.Actualizacion;
using MurciaDatos.Tests.Comunes;

namespace MurciaDatos.Api.Tests;

/// <summary>
/// La API completa en memoria, con una release de prueba (datos ficticios) como origen y una carpeta temporal
/// para las versiones descargadas. Cada fábrica tiene su propia configuración.
/// </summary>
public sealed class ApiDePrueba : WebApplicationFactory<Program>
{
    private readonly DirectorioTemporal _directorio = new();
    private readonly Dictionary<string, string?> _configuracion = [];

    public ApiDePrueba(
        Dictionary<string, string?>? configuracion = null,
        bool sinDatos = false,
        bool relojFalso = false,
        VarianteDeRelease? variante = null)
    {
        CarpetaDeRelease = _directorio.Subcarpeta("release");
        if (!sinDatos)
        {
            ReleaseDePrueba.Crear(CarpetaDeRelease, variante);
        }

        _configuracion["Datos:Origen"] = "Directorio";
        _configuracion["Datos:Ruta"] = CarpetaDeRelease;
        _configuracion["Datos:Directorio"] = _directorio.Subcarpeta("datos");
        _configuracion["Limites:PeticionesPorVentana"] = "100000";
        foreach (var (clave, valor) in configuracion ?? [])
        {
            _configuracion[clave] = valor;
        }

        if (relojFalso)
        {
            Reloj = new FakeTimeProvider(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
        }
    }

    public string CarpetaDeRelease { get; }

    public FakeTimeProvider? Reloj { get; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.UseStaticWebAssets();
        builder.ConfigureAppConfiguration((_, configuracion) => configuracion.AddInMemoryCollection(_configuracion));

        if (Reloj is not null)
        {
            builder.ConfigureServices(servicios => servicios.Replace(ServiceDescriptor.Singleton<TimeProvider>(Reloj)));
        }
    }

    /// <summary>Espera a que el actualizador de fondo haya cargado la primera versión (o a que se rinda).</summary>
    public async Task EsperarDatosAsync(bool esperados = true)
    {
        using var cliente = CreateClient();
        for (var i = 0; i < 100; i++)
        {
            using var respuesta = await cliente.GetAsync("/health/ready", TestContext.Current.CancellationToken);
            if (respuesta.IsSuccessStatusCode == esperados)
            {
                return;
            }

            await Task.Delay(100, TestContext.Current.CancellationToken);
        }
    }

    public ServicioDeActualizacion Actualizacion => Services.GetRequiredService<ServicioDeActualizacion>();

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _directorio.Dispose();
        }
    }
}

public static class Http
{
    public static async Task<JsonElement> JsonAsync(this HttpResponseMessage respuesta)
    {
        var texto = await respuesta.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return JsonDocument.Parse(texto).RootElement.Clone();
    }

    public static Task<HttpResponseMessage> PedirAsync(this HttpClient cliente, string ruta, params (string Cabecera, string Valor)[] cabeceras)
    {
        var peticion = new HttpRequestMessage(HttpMethod.Get, ruta);
        foreach (var (cabecera, valor) in cabeceras)
        {
            peticion.Headers.TryAddWithoutValidation(cabecera, valor);
        }

        return cliente.SendAsync(peticion, TestContext.Current.CancellationToken);
    }

    public static string Una(this HttpResponseMessage respuesta, string cabecera) =>
        respuesta.Headers.TryGetValues(cabecera, out var valores) ? string.Join(",", valores)
        : respuesta.Content.Headers.TryGetValues(cabecera, out var delContenido) ? string.Join(",", delContenido)
        : string.Empty;
}

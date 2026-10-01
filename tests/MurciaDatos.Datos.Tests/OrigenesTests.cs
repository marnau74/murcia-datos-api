using System.Net;
using System.Text;

using Microsoft.Extensions.Options;

using MurciaDatos.Datos.Origen;
using MurciaDatos.Tests.Comunes;

using Shouldly;

namespace MurciaDatos.Datos.Tests;

public class OrigenGitHubTests
{
    private static readonly string[] TodosLosFicheros = ["murcia_turismo.duckdb", "contrato.json", "SHA256SUMS", "dim_fecha.parquet"];

    private static string Release(string etiqueta, string[]? ficheros = null, bool borrador = false, bool preliminar = false) =>
        $$"""
        {"tag_name":"{{etiqueta}}","draft":{{borrador.ToString().ToLowerInvariant()}},"prerelease":{{preliminar.ToString().ToLowerInvariant()}},
         "assets":[{{string.Join(",", (ficheros ?? TodosLosFicheros).Select(f => $$"""{"name":"{{f}}","browser_download_url":"https://malicioso.example/{{f}}"}"""))}}]}
        """;

    private static (OrigenGitHub Origen, ManejadorFalso Manejador) Crear(string releasesJson, string repositorio = "marnau74/murcia-open-data")
    {
        var manejador = new ManejadorFalso(releasesJson);
        var origen = new OrigenGitHub(new HttpClient(manejador), Options.Create(new OpcionesDeDatos { Repositorio = repositorio }));
        return (origen, manejador);
    }

    [Fact]
    public async Task Elige_la_release_de_datos_mas_reciente_e_ignora_las_del_codigo_y_los_borradores()
    {
        var (origen, _) = Crear($"[{Release("v2.1.0")},{Release("datos-2026-08")},{Release("datos-2026-09")},{Release("datos-2026-10", borrador: true)},{Release("datos-2026-11", preliminar: true)}]");

        var paquete = await origen.ObtenerUltimoAsync(TestContext.Current.CancellationToken);

        paquete.ShouldNotBeNull().Etiqueta.ShouldBe("datos-2026-09");
    }

    [Fact]
    public async Task Ignora_las_releases_a_las_que_les_falta_algun_fichero_necesario()
    {
        var (origen, _) = Crear($"[{Release("datos-2026-09", ["contrato.json", "SHA256SUMS"])},{Release("datos-2026-08")}]");

        var paquete = await origen.ObtenerUltimoAsync(TestContext.Current.CancellationToken);

        paquete.ShouldNotBeNull().Etiqueta.ShouldBe("datos-2026-08");
    }

    [Theory]
    [InlineData("datos-2026-13")]
    [InlineData("datos-26-09")]
    [InlineData("datos-2026-09-b")]
    [InlineData("../datos-2026-09")]
    public async Task Las_etiquetas_con_formato_raro_no_se_aceptan(string etiqueta)
    {
        var (origen, _) = Crear($"[{Release(etiqueta)}]");

        (await origen.ObtenerUltimoAsync(TestContext.Current.CancellationToken)).ShouldBeNull();
    }

    [Fact]
    public async Task Sin_releases_de_datos_devuelve_null()
    {
        var (origen, _) = Crear("[]");

        (await origen.ObtenerUltimoAsync(TestContext.Current.CancellationToken)).ShouldBeNull();
    }

    [Fact]
    public async Task Descarga_solo_del_repositorio_configurado_sin_fiarse_de_las_direcciones_de_la_respuesta()
    {
        var (origen, manejador) = Crear($"[{Release("datos-2026-09")}]");
        var paquete = (await origen.ObtenerUltimoAsync(TestContext.Current.CancellationToken))!;

        await using var flujo = await paquete.Abrir("contrato.json", TestContext.Current.CancellationToken);

        manejador.Direcciones.ShouldContain("https://api.github.com/repos/marnau74/murcia-open-data/releases?per_page=30");
        manejador.Direcciones.ShouldContain("https://github.com/marnau74/murcia-open-data/releases/download/datos-2026-09/contrato.json");
        manejador.Direcciones.ShouldNotContain(d => d.Contains("malicioso", StringComparison.Ordinal));
    }

    [Fact]
    public async Task No_descarga_ficheros_que_no_son_del_paquete()
    {
        var (origen, _) = Crear($"[{Release("datos-2026-09")}]");
        var paquete = (await origen.ObtenerUltimoAsync(TestContext.Current.CancellationToken))!;

        await Should.ThrowAsync<ArgumentException>(() => paquete.Abrir("../../otra-cosa.exe", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Un_error_de_github_se_propaga_para_que_el_servicio_lo_anote()
    {
        var manejador = new ManejadorFalso("{}", HttpStatusCode.Forbidden);
        var origen = new OrigenGitHub(new HttpClient(manejador), Options.Create(new OpcionesDeDatos()));

        await Should.ThrowAsync<HttpRequestException>(() => origen.ObtenerUltimoAsync(TestContext.Current.CancellationToken));
    }

    private sealed class ManejadorFalso(string releasesJson, HttpStatusCode estado = HttpStatusCode.OK) : HttpMessageHandler
    {
        public List<string> Direcciones { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Direcciones.Add(request.RequestUri!.ToString());
            var contenido = request.RequestUri.Host == "api.github.com" ? releasesJson : "{}";
            return Task.FromResult(new HttpResponseMessage(estado) { Content = new StringContent(contenido, Encoding.UTF8, "application/json") });
        }
    }
}

public class OrigenDirectorioTests
{
    [Fact]
    public async Task Sirve_los_ficheros_de_la_carpeta_con_la_etiqueta_del_ultimo_mes_del_contrato()
    {
        using var directorio = new DirectorioTemporal();
        var carpeta = directorio.Subcarpeta("release");
        ReleaseDePrueba.Crear(carpeta);
        var origen = new OrigenDirectorio(Options.Create(new OpcionesDeDatos { Origen = TipoDeOrigen.Directorio, Ruta = carpeta }));

        var paquete = await origen.ObtenerUltimoAsync(TestContext.Current.CancellationToken);

        paquete.ShouldNotBeNull().Etiqueta.ShouldBe("datos-2024-08");
        await using var flujo = await paquete.Abrir("SHA256SUMS", TestContext.Current.CancellationToken);
        using var lector = new StreamReader(flujo);
        (await lector.ReadToEndAsync(TestContext.Current.CancellationToken)).ShouldContain("murcia_turismo.duckdb");
    }

    [Fact]
    public async Task Sin_carpeta_o_con_ficheros_a_medias_devuelve_null()
    {
        using var directorio = new DirectorioTemporal();
        var carpeta = directorio.Subcarpeta("release");
        File.WriteAllText(Path.Combine(carpeta, "contrato.json"), "{}");

        var incompleta = new OrigenDirectorio(Options.Create(new OpcionesDeDatos { Ruta = carpeta }));
        var inexistente = new OrigenDirectorio(Options.Create(new OpcionesDeDatos { Ruta = Path.Combine(carpeta, "no-existe") }));
        var sinRuta = new OrigenDirectorio(Options.Create(new OpcionesDeDatos()));

        (await incompleta.ObtenerUltimoAsync(TestContext.Current.CancellationToken)).ShouldBeNull();
        (await inexistente.ObtenerUltimoAsync(TestContext.Current.CancellationToken)).ShouldBeNull();
        (await sinRuta.ObtenerUltimoAsync(TestContext.Current.CancellationToken)).ShouldBeNull();
    }
}

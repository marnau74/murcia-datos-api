using System.Net;

using MurciaDatos.Datos.Actualizacion;
using MurciaDatos.Tests.Comunes;

using Shouldly;

namespace MurciaDatos.Api.Tests;

public class CacheTests(ApiCompartida compartida) : IClassFixture<ApiCompartida>
{
    private readonly HttpClient _cliente = compartida.Cliente;

    private const string Consulta = "/v1/demanda?territorio=region-murcia&tipo=hotel&residencia=total&desde=2024-01&hasta=2024-03";

    [Fact]
    public async Task Una_respuesta_correcta_lleva_etag_y_cache_control_publico()
    {
        using var respuesta = await _cliente.GetAsync(Consulta, TestContext.Current.CancellationToken);

        respuesta.Headers.ETag.ShouldNotBeNull().IsWeak.ShouldBeTrue();
        respuesta.Una("Cache-Control").ShouldBe("public, max-age=3600");
    }

    [Fact]
    public async Task Con_if_none_match_igual_al_etag_responde_304_sin_cuerpo()
    {
        using var primera = await _cliente.GetAsync(Consulta, TestContext.Current.CancellationToken);
        var etag = primera.Headers.ETag!.ToString();

        using var segunda = await _cliente.PedirAsync(Consulta, ("If-None-Match", etag));

        segunda.StatusCode.ShouldBe(HttpStatusCode.NotModified);
        (await segunda.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldBeEmpty();
        segunda.Headers.ETag!.ToString().ShouldBe(etag);
    }

    [Fact]
    public async Task Una_etiqueta_distinta_o_antigua_da_la_respuesta_completa()
    {
        using var respuesta = await _cliente.PedirAsync(Consulta, ("If-None-Match", "W/\"otra-version\""));

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await respuesta.JsonAsync()).GetProperty("datos").GetArrayLength().ShouldBe(3);
    }

    [Fact]
    public async Task La_misma_consulta_escrita_de_otra_manera_tiene_el_mismo_etag()
    {
        using var a = await _cliente.GetAsync("/v1/demanda?territorio=costa-calida,cartagena&tipo=hotel&residencia=total&desde=2024-01&hasta=2024-03", TestContext.Current.CancellationToken);
        using var b = await _cliente.GetAsync("/v1/demanda?hasta=2024-03&desde=2024-01&residencia=TOTAL&tipo=hotel&territorio=cartagena,Costa-Calida", TestContext.Current.CancellationToken);

        b.Headers.ETag.ShouldBe(a.Headers.ETag);
    }

    [Fact]
    public async Task Consultas_distintas_o_formatos_distintos_tienen_etags_distintos()
    {
        using var json = await _cliente.GetAsync(Consulta, TestContext.Current.CancellationToken);
        using var csv = await _cliente.GetAsync(Consulta + "&formato=csv", TestContext.Current.CancellationToken);
        using var otraConsulta = await _cliente.GetAsync(Consulta.Replace("2024-03", "2024-04", StringComparison.Ordinal), TestContext.Current.CancellationToken);

        new[] { json.Headers.ETag, csv.Headers.ETag, otraConsulta.Headers.ETag }.Distinct().Count().ShouldBe(3);
    }

    [Fact]
    public async Task Un_etag_de_json_no_vale_para_pedir_csv_aunque_la_ruta_sea_la_misma()
    {
        using var json = await _cliente.GetAsync(Consulta, TestContext.Current.CancellationToken);

        using var csv = await _cliente.PedirAsync(Consulta, ("Accept", "text/csv"), ("If-None-Match", json.Headers.ETag!.ToString()));

        csv.StatusCode.ShouldBe(HttpStatusCode.OK);
        csv.Content.Headers.ContentType!.MediaType.ShouldBe("text/csv");
    }

    [Fact]
    public async Task Los_errores_no_llevan_cabeceras_de_cache_ni_se_guardan()
    {
        using var error = await _cliente.GetAsync("/v1/demanda?tipo=castillo", TestContext.Current.CancellationToken);

        error.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        error.Headers.ETag.ShouldBeNull();
        error.Una("Cache-Control").ShouldNotContain("public");
    }

    [Fact]
    public async Task Un_etag_valido_no_sirve_para_saltarse_la_validacion()
    {
        // El etag depende solo de la versión y de la consulta: conocerlo no debe dar un 304 a una consulta inválida.
        using var respuesta = await _cliente.PedirAsync("/v1/demanda?tipo=castillo", ("If-None-Match", "*"));

        respuesta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Los_catalogos_y_metadatos_tambien_se_cachean_con_etag()
    {
        foreach (var ruta in new[] { "/v1/metadatos", "/v1/territorios", "/v1/tipos-alojamiento", "/v1/residencias", "/v1/indicadores/estacionalidad", "/v1/indicadores/variacion" })
        {
            using var primera = await _cliente.GetAsync(ruta, TestContext.Current.CancellationToken);
            using var segunda = await _cliente.PedirAsync(ruta, ("If-None-Match", primera.Headers.ETag!.ToString()));

            primera.StatusCode.ShouldBe(HttpStatusCode.OK, ruta);
            segunda.StatusCode.ShouldBe(HttpStatusCode.NotModified, ruta);
        }
    }

    [Fact]
    public async Task Al_llegar_datos_nuevos_la_cache_se_vacia_y_el_etag_cambia()
    {
        using var api = new ApiDePrueba();
        await api.EsperarDatosAsync();
        using var cliente = api.CreateClient();
        const string ruta = "/v1/demanda?territorio=region-murcia&tipo=hotel&residencia=espana&desde=2024-01&hasta=2024-01&medidas=viajeros";

        using var antes = await cliente.GetAsync(ruta, TestContext.Current.CancellationToken);
        using var antesOtraVez = await cliente.GetAsync(ruta, TestContext.Current.CancellationToken); // ya sale de la caché
        var valorAntes = (await antes.JsonAsync()).GetProperty("datos")[0].GetProperty("viajeros").GetInt64();

        // El pipeline publica de nuevo la release del mes (misma etiqueta, otros datos).
        ReleaseDePrueba.Crear(api.CarpetaDeRelease, new VarianteDeRelease { Escala = 2 });
        (await api.Actualizacion.ActualizarAsync(TestContext.Current.CancellationToken)).ShouldBe(ResultadoDeActualizacion.Actualizado);

        using var despues = await cliente.PedirAsync(ruta, ("If-None-Match", antes.Headers.ETag!.ToString()));

        despues.StatusCode.ShouldBe(HttpStatusCode.OK); // el etag viejo ya no vale
        despues.Headers.ETag.ShouldNotBe(antes.Headers.ETag);
        (await despues.JsonAsync()).GetProperty("datos")[0].GetProperty("viajeros").GetInt64().ShouldBe(valorAntes * 2);
        antesOtraVez.Headers.ETag.ShouldBe(antes.Headers.ETag);
    }
}

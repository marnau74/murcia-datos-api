using System.Net;

using Shouldly;

namespace MurciaDatos.Api.Tests;

public class CatalogosTests(ApiCompartida compartida) : IClassFixture<ApiCompartida>
{
    private readonly HttpClient _cliente = compartida.Cliente;

    [Fact]
    public async Task Metadatos_dice_que_version_se_sirve_de_donde_viene_y_que_controles_ha_pasado()
    {
        using var respuesta = await _cliente.GetAsync("/v1/metadatos", TestContext.Current.CancellationToken);
        var json = await respuesta.JsonAsync();

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);
        json.GetProperty("version_datos").GetString().ShouldBe("datos-2024-08");
        json.GetProperty("version_contrato").GetString().ShouldBe("1.0.0");
        json.GetProperty("periodo").GetProperty("hasta").GetString().ShouldBe("2024-08");
        json.GetProperty("disponibilidad").GetProperty("demanda").GetProperty("provisional_desde").GetString().ShouldBe("2024-06");
        json.GetProperty("licencia").GetString()!.ShouldContain("INE");

        var controles = json.GetProperty("calidad").EnumerateArray().ToList();
        controles.Count.ShouldBe(6);
        controles.ShouldAllBe(c => c.GetProperty("correcto").GetBoolean());
        json.GetProperty("problema_actualizacion").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
    }

    [Fact]
    public async Task Territorios_trae_la_jerarquia_y_los_recursos_con_datos()
    {
        using var respuesta = await _cliente.GetAsync("/v1/territorios", TestContext.Current.CancellationToken);
        var datos = (await respuesta.JsonAsync()).GetProperty("datos").EnumerateArray().ToList();

        datos.Count.ShouldBe(5);
        var murcia = datos.Single(t => t.GetProperty("id").GetString() == "region-murcia");
        murcia.GetProperty("nivel").GetString().ShouldBe("region");
        murcia.GetProperty("padre").GetString().ShouldBe("espana");
        murcia.GetProperty("nombre").GetString().ShouldBe("Región de Murcia"); // tildes tal cual, sin escapar
        murcia.GetProperty("con_datos_de").EnumerateArray().Select(e => e.GetString()).ShouldBe(["demanda", "oferta", "precios"], ignoreOrder: true);

        var espana = datos.Single(t => t.GetProperty("id").GetString() == "espana");
        espana.GetProperty("padre").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
        espana.GetProperty("con_datos_de").EnumerateArray().Select(e => e.GetString()).ShouldBe(["precios"]);

        datos.Single(t => t.GetProperty("id").GetString() == "destino-la-manga").GetProperty("fuente").GetString().ShouldBe("murciaturistica");
    }

    [Fact]
    public async Task Tipos_y_residencias_se_listan()
    {
        var tipos = (await (await _cliente.GetAsync("/v1/tipos-alojamiento", TestContext.Current.CancellationToken)).JsonAsync()).GetProperty("datos").EnumerateArray();
        var residencias = (await (await _cliente.GetAsync("/v1/residencias", TestContext.Current.CancellationToken)).JsonAsync()).GetProperty("datos").EnumerateArray();

        tipos.Select(t => t.GetProperty("id").GetString()).ShouldBe(["apartamento", "camping", "hotel", "rural"], ignoreOrder: true);
        residencias.Select(r => r.GetProperty("id").GetString()).ShouldBe(["espana", "extranjero"], ignoreOrder: true);
    }

    [Fact]
    public async Task Medidas_lista_cada_medida_con_su_agregado_y_si_solo_existe_en_mensual()
    {
        var datos = (await (await _cliente.GetAsync("/v1/medidas", TestContext.Current.CancellationToken)).JsonAsync()).GetProperty("datos").EnumerateArray().ToList();

        datos.Count.ShouldBe(18);
        var viajeros = datos.Single(m => m.GetProperty("id").GetString() == "viajeros");
        viajeros.GetProperty("recurso").GetString().ShouldBe("demanda");
        viajeros.GetProperty("agregado").GetString().ShouldBe("suma");
        datos.Single(m => m.GetProperty("id").GetString() == "plazas").GetProperty("agregado").GetString().ShouldBe("media");
        datos.Where(m => m.GetProperty("solo_mensual").GetBoolean()).Select(m => m.GetProperty("id").GetString()).ShouldBe(["variacion_interanual"]);
    }

    [Fact]
    public async Task Los_tipos_dicen_en_que_recursos_tienen_datos()
    {
        var datos = (await (await _cliente.GetAsync("/v1/tipos-alojamiento", TestContext.Current.CancellationToken)).JsonAsync()).GetProperty("datos").EnumerateArray().ToList();

        datos.Single(t => t.GetProperty("id").GetString() == "hotel").GetProperty("con_datos_de").EnumerateArray().Select(e => e.GetString()).ShouldBe(["demanda", "oferta", "precios"], ignoreOrder: true);
        datos.Single(t => t.GetProperty("id").GetString() == "camping").GetProperty("con_datos_de").EnumerateArray().Select(e => e.GetString()).ShouldBe(["oferta"]);
        datos.Single(t => t.GetProperty("id").GetString() == "rural").GetProperty("con_datos_de").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Una_ruta_que_no_existe_bajo_v1_es_un_404_en_formato_problem_json()
    {
        using var respuesta = await _cliente.GetAsync("/v1/no-existe", TestContext.Current.CancellationToken);

        respuesta.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        respuesta.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task Los_metodos_que_no_son_de_lectura_no_se_admiten()
    {
        using var respuesta = await _cliente.PostAsync("/v1/demanda", new StringContent("{}"), TestContext.Current.CancellationToken);

        respuesta.StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public async Task El_explorador_se_sirve_en_la_raiz_y_las_rutas_del_cliente_vuelven_a_su_pagina()
    {
        using var raiz = await _cliente.GetAsync("/", TestContext.Current.CancellationToken);
        using var ruta = await _cliente.GetAsync("/alguna/ruta/del/explorador", TestContext.Current.CancellationToken);

        raiz.StatusCode.ShouldBe(HttpStatusCode.OK);
        raiz.Content.Headers.ContentType!.MediaType.ShouldBe("text/html");
        ruta.StatusCode.ShouldBe(HttpStatusCode.OK);
        ruta.Content.Headers.ContentType!.MediaType.ShouldBe("text/html");
    }
}

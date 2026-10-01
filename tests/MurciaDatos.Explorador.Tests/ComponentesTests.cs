using System.Text.Json;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

using MurciaDatos.Explorador.Componentes;
using MurciaDatos.Explorador.Logica;
using MurciaDatos.Explorador.Pages;
using MurciaDatos.Explorador.Servicios;

using Shouldly;

namespace MurciaDatos.Explorador.Tests;

public class GraficaDeLineasTests : BunitContext
{
    private static SerieGrafica Serie(string nombre, params (string Periodo, double? Valor, bool Incompleto)[] puntos) =>
        new(nombre, [.. puntos.Select(p => new PuntoDeGrafica(p.Periodo, p.Valor, p.Incompleto, p.Incompleto ? 8 : null))]);

    [Fact]
    public void Pinta_una_linea_por_serie_con_su_leyenda_y_un_resumen_accesible()
    {
        var series = new[]
        {
            Serie("Hoteles", ("2024-01", 100, false), ("2024-02", 200, false)),
            Serie("Campings", ("2024-01", 50, false), ("2024-02", 80, false)),
        };

        var cut = Render<GraficaDeLineas>(p => p.Add(c => c.Series, series).Add(c => c.Unidad, "noches").Add(c => c.Titulo, "Pernoctaciones"));

        cut.FindAll("g.serie").Count.ShouldBe(2);
        cut.FindAll(".leyenda li").Select(l => l.TextContent.Trim()).ShouldBe(["Hoteles", "Campings"]);
        cut.Find("svg[role=img]").GetAttribute("aria-labelledby").ShouldNotBeNullOrWhiteSpace();
        cut.Find("title").TextContent.ShouldBe("Pernoctaciones");
        cut.Find("desc").TextContent.ShouldContain("2 serie(s)");
        cut.Markup.ShouldContain("Unidad: noches");
    }

    [Fact]
    public void Un_hueco_en_los_datos_corta_el_trazo()
    {
        var cut = Render<GraficaDeLineas>(p => p
            .Add(c => c.Series, [Serie("a", ("1", 10, false), ("2", null, false), ("3", 30, false))])
            .Add(c => c.Unidad, "x"));

        var trazo = cut.Find("g.serie path").GetAttribute("d")!;

        trazo.Count(c => c == 'M').ShouldBe(2);
        trazo.ShouldNotContain("L");
    }

    [Fact]
    public void El_valor_de_cada_punto_esta_en_su_titulo_con_formato_espanol_y_marca_los_periodos_incompletos()
    {
        var cut = Render<GraficaDeLineas>(p => p
            .Add(c => c.Series, [Serie("Hoteles", ("2025", 1234567, false), ("2026", 800000, true))])
            .Add(c => c.Unidad, "noches"));

        var titulos = cut.FindAll("circle title").Select(t => t.TextContent).ToList();

        titulos[0].ShouldBe("Hoteles · 2025: 1.234.567");
        titulos[1].ShouldContain("periodo incompleto: 8 meses con dato");
        cut.FindAll("circle.incompleto").Count.ShouldBe(1);
        cut.Markup.ShouldContain("Círculo vacío");
    }

    [Fact]
    public void Sin_datos_dice_que_no_hay_datos_en_lugar_de_pintar_una_grafica_vacia()
    {
        var cut = Render<GraficaDeLineas>(p => p.Add(c => c.Series, [Serie("a", ("1", null, false))]));

        cut.Find("p.vacio").TextContent.ShouldContain("No hay datos");
        cut.FindAll("svg").Count.ShouldBe(0);
    }

    [Fact]
    public void Las_marcas_del_eje_usan_el_texto_corto_y_no_llevan_estilos_en_linea()
    {
        var cut = Render<GraficaDeLineas>(p => p.Add(c => c.Series, [Serie("a", ("1", 1_000_000, false), ("2", 2_000_000, false))]).Add(c => c.Unidad, "noches"));

        cut.FindAll("text.eje").Select(t => t.TextContent).ShouldContain("2 M");
        cut.Markup.ShouldNotContain("style=");
    }
}

public class HomeTests : BunitContext
{
    private static readonly Catalogos Catalogos = new(
        new MetadatosApi(
            "datos-2026-08",
            ["INE"],
            new Dictionary<string, RangoApi>
            {
                ["demanda"] = new("2015-01", "2024-03", "2024-03"),
                ["oferta"] = new("2015-01", "2024-03", null),
                ["precios"] = new("2015-01", "2024-03", null),
            }),
        [
            new TerritorioApi("espana", "España", "pais", null, "INE", ["precios"]),
            new TerritorioApi("region-murcia", "Región de Murcia", "region", "espana", "INE", ["demanda", "oferta", "precios"]),
            new TerritorioApi("cartagena", "Cartagena", "punto_ine", "region-murcia", "INE", ["demanda", "oferta"]),
        ],
        [new OpcionApi("hotel", "Hoteles", ["demanda", "oferta", "precios"]), new OpcionApi("camping", "Campings", ["oferta"])],
        [new OpcionApi("espana", "Residentes en España", ["demanda"]), new OpcionApi("extranjero", "Residentes en el extranjero", ["demanda"])],
        [
            new MedidaApi("demanda", "viajeros", "personas", "suma", "Viajeros alojados", false),
            new MedidaApi("demanda", "pernoctaciones", "noches", "suma", "Pernoctaciones", false),
            new MedidaApi("oferta", "ocupacion_plazas", "%", "media", "Grado de ocupación por plazas", false),
            new MedidaApi("precios", "indice_precios", "índice", "media", "Índice de precios hoteleros", false),
            new MedidaApi("precios", "variacion_interanual", "%", "media", "Variación interanual", true),
        ]);

    private sealed class ClienteFalso(Func<string, string, RespuestaSerieApi> serie) : IClienteApi
    {
        public List<(string Recurso, string Consulta)> Llamadas { get; } = [];

        public Task<Catalogos> CargarCatalogosAsync(CancellationToken cancelacion = default) => Task.FromResult(Catalogos);

        public Task<RespuestaSerieApi> CargarSerieAsync(string recurso, string consulta, CancellationToken cancelacion = default)
        {
            Llamadas.Add((recurso, consulta));
            return Task.FromResult(serie(recurso, consulta));
        }
    }

    private static RespuestaSerieApi Respuesta(string json) => JsonSerializer.Deserialize<RespuestaSerieApi>(json)!;

    private const string SerieDeDemanda = """
        {"datos":[
          {"periodo":"2024-01","territorio":"region-murcia","tipo":"hotel","residencia":"total","fuente":"INE","provisional":false,"pernoctaciones":150000},
          {"periodo":"2024-02","territorio":"region-murcia","tipo":"hotel","residencia":"total","fuente":"INE","provisional":false,"pernoctaciones":null},
          {"periodo":"2024-03","territorio":"region-murcia","tipo":"hotel","residencia":"total","fuente":"INE","provisional":true,"pernoctaciones":200000}],
         "meta":{"version_datos":"datos-2026-08","provisional_desde":"2024-03","total_filas":3}}
        """;

    private ClienteFalso Preparar(Func<string, string, RespuestaSerieApi>? serie = null)
    {
        var cliente = new ClienteFalso(serie ?? ((_, _) => Respuesta(SerieDeDemanda)));
        Services.AddSingleton<IClienteApi>(cliente);
        JSInterop.Mode = JSRuntimeMode.Loose;
        return cliente;
    }

    [Fact]
    public void Al_abrir_pide_la_serie_por_defecto_y_muestra_grafica_tabla_y_llamada_equivalente()
    {
        var cliente = Preparar();

        var cut = Render<Home>();
        cut.WaitForAssertion(() => cut.FindAll("table tbody tr").Count.ShouldBe(3));

        cliente.Llamadas.ShouldContain(("demanda", "territorio=region-murcia&tipo=hotel&residencia=total&desde=2019-01&agregacion=mes&medidas=pernoctaciones"));
        cut.Find(".datos-version").TextContent.ShouldContain("datos-2026-08");
        cut.Find(".datos-version").TextContent.ShouldContain("provisionales desde 2024-03");
        cut.FindAll("g.serie").Count.ShouldBe(1);
        cut.Find(".bloque pre").TextContent.ShouldContain("curl -s \"http://localhost/v1/demanda?territorio=region-murcia");
    }

    [Fact]
    public void Un_mes_sin_dato_sale_como_raya_en_la_tabla_y_los_provisionales_se_marcan()
    {
        Preparar();

        var cut = Render<Home>();
        cut.WaitForAssertion(() => cut.FindAll("table tbody tr").Count.ShouldBe(3));

        var filas = cut.FindAll("table tbody tr");
        filas[0].QuerySelectorAll("td")[^1].TextContent.ShouldContain("150.000");
        filas[1].QuerySelectorAll("td")[^1].TextContent.Trim().ShouldBe("—");
        filas[2].QuerySelectorAll("td")[^1].TextContent.ShouldContain("prov.");
    }

    [Fact]
    public void La_url_manda_sobre_los_valores_por_defecto()
    {
        var cliente = Preparar();
        Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>()
            .NavigateTo("/?recurso=oferta&territorio=cartagena&tipo=hotel,camping&medida=ocupacion_plazas&agregacion=anio&desde=2020-01");

        var cut = Render<Home>();
        cut.WaitForAssertion(() => cliente.Llamadas.Count.ShouldBeGreaterThan(0));

        cliente.Llamadas[0].ShouldBe(("oferta", "territorio=cartagena&tipo=hotel,camping&desde=2020-01&agregacion=anio&medidas=ocupacion_plazas"));
    }

    [Fact]
    public void Una_url_con_valores_que_no_existen_para_ese_recurso_se_corrige_antes_de_llamar_a_la_api()
    {
        var cliente = Preparar();
        // España no tiene demanda y «castillo» no existe: se pasa a la región y a hoteles.
        Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>()
            .NavigateTo("/?recurso=demanda&territorio=espana&tipo=castillo&medida=beneficio&desde=1999-01");

        var cut = Render<Home>();
        cut.WaitForAssertion(() => cliente.Llamadas.Count.ShouldBeGreaterThan(0));

        cliente.Llamadas[0].Consulta.ShouldBe("territorio=region-murcia&tipo=hotel&residencia=total&agregacion=mes&medidas=viajeros");
    }

    [Fact]
    public void Cambiar_de_recurso_vuelve_a_pedir_con_la_medida_de_ese_recurso_y_actualiza_la_url()
    {
        var cliente = Preparar();
        var cut = Render<Home>();
        cut.WaitForAssertion(() => cut.FindAll("table tbody tr").Count.ShouldBe(3));

        cut.FindAll("input[name=recurso]").Single(i => i.GetAttribute("value") == "oferta").Change("oferta");

        cut.WaitForAssertion(() => cliente.Llamadas.Last().Recurso.ShouldBe("oferta"));
        cliente.Llamadas.Last().Consulta.ShouldContain("medidas=ocupacion_plazas");
        Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().Uri.ShouldContain("recurso=oferta");
    }

    [Fact]
    public void Siempre_queda_al_menos_un_territorio_marcado()
    {
        var cliente = Preparar();
        var cut = Render<Home>();
        cut.WaitForAssertion(() => cut.FindAll("table tbody tr").Count.ShouldBe(3));

        var marcado = cut.FindAll(".casilla input[type=checkbox]").First(c => c.HasAttribute("checked"));
        marcado.Change(false);

        cut.FindAll(".lista-larga input[type=checkbox]").Count(c => c.HasAttribute("checked")).ShouldBe(1);
    }

    [Fact]
    public void Los_territorios_se_listan_en_orden_de_jerarquia_y_solo_los_que_tienen_datos()
    {
        Preparar();

        var cut = Render<Home>();
        cut.WaitForAssertion(() => cut.FindAll(".lista-larga .casilla").Count.ShouldBeGreaterThan(0));

        cut.FindAll(".lista-larga .casilla").Select(c => c.TextContent.Trim()).ShouldBe(["Región de Murcia INE", "Cartagena INE"]);
    }

    [Fact]
    public void Un_destino_cuya_zona_no_tiene_datos_se_cuelga_de_la_region()
    {
        var territorios = new List<TerritorioApi>
        {
            new("region-murcia", "Región de Murcia", "region", "espana", "INE", ["demanda"]),
            new("zona-costa", "Costa", "zona_mt", "region-murcia", "murciaturistica", []),
            new("destino-la-manga", "La Manga", "destino_mt", "zona-costa", "murciaturistica", ["demanda"]),
            new("cartagena", "Cartagena", "punto_ine", "region-murcia", "INE", ["demanda"]),
        };

        var orden = Home.EnJerarquia(territorios, t => t.ConDatosDe.Contains("demanda")).Select(t => (t.Territorio.Id, t.Nivel)).ToList();

        orden.ShouldBe([("region-murcia", 0), ("cartagena", 1), ("destino-la-manga", 1)]);
    }

    [Fact]
    public void Un_error_de_la_api_se_enseña_con_el_detalle_por_parametro()
    {
        Preparar((_, _) => throw new ErrorDeApiException("Parámetros no válidos", "Alguno no es válido.", new Dictionary<string, string[]> { ["desde"] = ["no es un mes válido"] }));

        var cut = Render<Home>();
        cut.WaitForAssertion(() => cut.Find(".resultado .aviso-error").TextContent.ShouldContain("Parámetros no válidos"));

        cut.Find(".resultado .aviso-error li").TextContent.ShouldContain("desde");
        cut.Find(".resultado [role=alert]").ShouldNotBeNull();
    }

    [Fact]
    public void Si_no_hay_conexion_con_la_api_al_cargar_el_catalogo_ofrece_reintentar()
    {
        Services.AddSingleton<IClienteApi>(new CatalogoQueFalla());
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = Render<Home>();

        cut.WaitForAssertion(() => cut.Find(".aviso-error").TextContent.ShouldContain("No se han podido cargar los datos"));
        cut.Find(".aviso-error button").TextContent.ShouldBe("Reintentar");
    }

    [Fact]
    public void Copiar_la_llamada_usa_el_portapapeles_del_navegador()
    {
        Preparar();
        var copia = JSInterop.SetupVoid("explorador.copiar", _ => true);
        copia.SetVoidResult();
        var cut = Render<Home>();
        cut.WaitForAssertion(() => cut.FindAll(".bloque button").Count.ShouldBe(3));

        cut.FindAll(".bloque button")[0].Click();

        cut.WaitForAssertion(() => cut.FindAll(".bloque button")[0].TextContent.ShouldBe("Copiado"));
        copia.Invocations.Single().Arguments[0]!.ToString()!.ShouldStartWith("curl -s");
    }

    [Fact]
    public void Con_mas_de_cien_filas_la_tabla_se_corta_y_ofrece_mostrarlas_todas()
    {
        var filas = string.Join(",", Enumerable.Range(0, 130).Select(i =>
            $$"""{"periodo":"p{{i:000}}","territorio":"region-murcia","tipo":"hotel","residencia":"total","fuente":"INE","provisional":false,"pernoctaciones":{{i + 1}}}"""));
        Preparar((_, _) => Respuesta("{\"datos\":[" + filas + "],\"meta\":{\"version_datos\":\"v\",\"provisional_desde\":null,\"total_filas\":130}}"));

        var cut = Render<Home>();
        cut.WaitForAssertion(() => cut.FindAll("table tbody tr").Count.ShouldBe(100));

        cut.Find(".resultado > button").Click();

        cut.FindAll("table tbody tr").Count.ShouldBe(130);
    }

    private sealed class CatalogoQueFalla : IClienteApi
    {
        public Task<Catalogos> CargarCatalogosAsync(CancellationToken cancelacion = default) => throw new HttpRequestException("sin red");

        public Task<RespuestaSerieApi> CargarSerieAsync(string recurso, string consulta, CancellationToken cancelacion = default) => throw new HttpRequestException("sin red");
    }
}

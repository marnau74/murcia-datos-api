using System.Text.Json;

using MurciaDatos.Explorador.Logica;
using MurciaDatos.Explorador.Servicios;

using Shouldly;

namespace MurciaDatos.Explorador.Tests;

public class EstadoDeConsultaTests
{
    [Fact]
    public void La_demanda_por_defecto_pide_el_total_de_pernoctaciones_de_los_hoteles_de_la_region()
    {
        EstadoDeConsulta.Inicial("demanda").ConsultaApi().ShouldBe(
            "territorio=region-murcia&tipo=hotel&residencia=total&desde=2019-01&agregacion=mes&medidas=pernoctaciones");
    }

    [Fact]
    public void La_oferta_y_los_precios_no_envian_residencia_porque_la_api_no_la_admite()
    {
        EstadoDeConsulta.Inicial("oferta").ConsultaApi().ShouldNotContain("residencia");
        EstadoDeConsulta.Inicial("precios").ConsultaApi().ShouldNotContain("residencia");
        EstadoDeConsulta.Inicial("oferta").ConsultaApi().ShouldContain("medidas=ocupacion_plazas");
        EstadoDeConsulta.Inicial("precios").ConsultaApi().ShouldContain("medidas=indice_precios");
    }

    [Fact]
    public void Las_dos_residencias_por_separado_se_piden_como_lista()
    {
        var estado = EstadoDeConsulta.Inicial("demanda") with { Residencia = "separadas" };

        estado.ConsultaApi().ShouldContain("residencia=espana,extranjero");
    }

    [Fact]
    public void El_csv_y_la_variante_para_excel_anaden_sus_parametros_al_final()
    {
        var estado = EstadoDeConsulta.Inicial("demanda");

        estado.ConsultaApi("csv").ShouldEndWith("&formato=csv");
        estado.ConsultaApi("csv", excel: true).ShouldEndWith("&formato=csv&excel=true");
        estado.ConsultaApi().ShouldNotContain("formato");
    }

    [Fact]
    public void Desde_y_hasta_vacios_no_se_envian()
    {
        var estado = EstadoDeConsulta.Inicial("demanda") with { Desde = null, Hasta = null };

        estado.ConsultaApi().ShouldNotContain("desde");
        estado.ConsultaApi().ShouldNotContain("hasta");
    }

    [Fact]
    public void El_estado_viaja_en_la_url_de_la_pagina_y_se_reconstruye_igual()
    {
        var estado = new EstadoDeConsulta
        {
            Recurso = "demanda",
            Territorios = ["costa-calida", "cartagena"],
            Tipos = ["hotel", "apartamento"],
            Residencia = "separadas",
            Medida = "viajeros",
            Agregacion = "trimestre",
            Desde = "2020-01",
            Hasta = "2024-12",
        };

        var reconstruido = EstadoDeConsulta.DesdeLaPagina(estado.ParaLaPagina());

        reconstruido.EquivaleA(estado).ShouldBeTrue();
        reconstruido.Territorios.ShouldBe(["costa-calida", "cartagena"]);
        reconstruido.Hasta.ShouldBe("2024-12");
        estado.ParaLaPagina().ShouldBe("?recurso=demanda&territorio=costa-calida,cartagena&tipo=hotel,apartamento&residencia=separadas&medida=viajeros&agregacion=trimestre&desde=2020-01&hasta=2024-12");
    }

    [Theory]
    [InlineData("")]
    [InlineData("?")]
    [InlineData("?recurso=nada&agregacion=semana&residencia=marte&desde=ayer&hasta=2024-13")]
    public void Una_url_vacia_o_con_basura_vuelve_a_los_valores_por_defecto(string consulta)
    {
        var estado = EstadoDeConsulta.DesdeLaPagina(consulta);

        estado.Recurso.ShouldBe("demanda");
        estado.Agregacion.ShouldBe("mes");
        estado.Residencia.ShouldBe("total");
        estado.Hasta.ShouldBeNull();
        estado.Desde.ShouldBe("2019-01");
    }

    [Fact]
    public void Un_desde_vacio_en_la_url_significa_desde_el_primer_mes_y_no_el_valor_por_defecto()
    {
        EstadoDeConsulta.DesdeLaPagina("?recurso=demanda&territorio=region-murcia").Desde.ShouldBe("2019-01");
        EstadoDeConsulta.DesdeLaPagina("?recurso=demanda&desde=").Desde.ShouldBeNull();
    }

    [Fact]
    public void Elegir_desde_el_primer_mes_sobrevive_a_la_ida_y_vuelta_por_la_url()
    {
        var estado = EstadoDeConsulta.Inicial("demanda") with { Desde = null };

        estado.ParaLaPagina().ShouldEndWith("desde=");
        EstadoDeConsulta.DesdeLaPagina(estado.ParaLaPagina()).Desde.ShouldBeNull();
        EstadoDeConsulta.DesdeLaPagina(estado.ParaLaPagina()).EquivaleA(estado).ShouldBeTrue();
    }

    [Fact]
    public void Cada_recurso_arranca_con_su_propia_medida()
    {
        EstadoDeConsulta.DesdeLaPagina("?recurso=oferta").Medida.ShouldBe("ocupacion_plazas");
        EstadoDeConsulta.DesdeLaPagina("?recurso=precios").Medida.ShouldBe("indice_precios");
    }

    [Theory]
    [InlineData("2024-01", true)]
    [InlineData("1899-12", false)]
    [InlineData("2024-00", false)]
    [InlineData("2024-1", false)]
    [InlineData("24-01", false)]
    [InlineData("abcd-ef", false)]
    [InlineData(null, false)]
    public void Reconoce_los_meses_validos(string? texto, bool valido)
    {
        EstadoDeConsulta.EsMes(texto).ShouldBe(valido);
    }
}

public class FormatoTests
{
    [Theory]
    [InlineData(1234567.0, 0, "1.234.567")]
    [InlineData(1234.5, 2, "1.234,50")]
    [InlineData(0.0, 0, "0")]
    [InlineData(-9876.543, 1, "-9.876,5")]
    public void Los_numeros_se_escriben_a_la_espanola(double valor, int decimales, string esperado)
    {
        Formato.Numero(valor, decimales).ShouldBe(esperado);
    }

    [Theory]
    [InlineData(155352.0, "noches", "155.352")]
    [InlineData(55.5, "%", "55,5 %")]
    [InlineData(134.6, "índice", "134,60")]
    [InlineData(2.5, "plazas", "2,50")]
    public void Cada_unidad_tiene_sus_decimales(double valor, string unidad, string esperado)
    {
        Formato.Valor(valor, unidad).ShouldBe(esperado);
    }

    [Fact]
    public void Un_dato_que_falta_se_muestra_como_raya_y_no_como_cero()
    {
        Formato.Valor(null, "noches").ShouldBe("—");
    }

    [Theory]
    [InlineData(500000.0, "500 mil")]
    [InlineData(1500000.0, "1,5 M")]
    [InlineData(2000000.0, "2 M")]
    [InlineData(250.0, "250")]
    [InlineData(12.5, "12,5")]
    public void Las_marcas_de_los_ejes_son_cortas(double valor, string esperado)
    {
        Formato.Corto(valor).ShouldBe(esperado);
    }
}

public class EscalaDeEjesTests
{
    [Fact]
    public void Los_ejes_con_base_cero_empiezan_en_cero_y_usan_pasos_redondos()
    {
        var (minimo, maximo, marcas) = EscalaDeEjes.Calcular(120_000, 455_000, baseCero: true);

        minimo.ShouldBe(0);
        maximo.ShouldBe(500_000);
        marcas.ShouldBe([0, 100_000, 200_000, 300_000, 400_000, 500_000]);
    }

    [Fact]
    public void Un_indice_no_tiene_por_que_empezar_en_cero()
    {
        var (minimo, maximo, _) = EscalaDeEjes.Calcular(98, 187, baseCero: false);

        minimo.ShouldBeGreaterThan(0);
        minimo.ShouldBeLessThanOrEqualTo(98);
        maximo.ShouldBeGreaterThanOrEqualTo(187);
    }

    [Fact]
    public void Una_serie_plana_abre_un_hueco_para_que_la_linea_quede_visible()
    {
        var (minimo, maximo, marcas) = EscalaDeEjes.Calcular(50, 50, baseCero: false);

        maximo.ShouldBeGreaterThan(minimo);
        marcas.Count.ShouldBeGreaterThan(1);
    }

    [Fact]
    public void Todo_ceros_no_divide_por_cero()
    {
        var (minimo, maximo, marcas) = EscalaDeEjes.Calcular(0, 0, baseCero: true);

        maximo.ShouldBeGreaterThan(minimo);
        marcas.ShouldContain(0);
    }

    [Theory]
    [InlineData(0.8, 1)]
    [InlineData(1.7, 2)]
    [InlineData(3.2, 5)]
    [InlineData(7.0, 10)]
    [InlineData(23_000, 50_000)]
    public void El_paso_siempre_es_1_2_5_o_10_por_una_potencia_de_diez(double bruto, double esperado)
    {
        EscalaDeEjes.PasoRedondo(bruto).ShouldBe(esperado, 1e-9);
    }
}

public class GeometriaDeGraficaTests
{
    private static SerieGrafica Serie(string nombre, params (string Periodo, double? Valor)[] puntos) =>
        new(nombre, [.. puntos.Select(p => new PuntoDeGrafica(p.Periodo, p.Valor))]);

    [Fact]
    public void Un_mes_sin_dato_corta_la_linea_en_dos_en_lugar_de_unir_los_puntos_vecinos()
    {
        var serie = Serie("a", ("2024-01", 10), ("2024-02", 20), ("2024-03", null), ("2024-04", 30), ("2024-05", 40));
        var geometria = new GeometriaDeGrafica([serie], baseCero: true);

        var trazo = geometria.Trazo(serie);

        trazo.Count(c => c == 'M').ShouldBe(2); // dos tramos
        trazo.Count(c => c == 'L').ShouldBe(2);
    }

    [Fact]
    public void Un_punto_aislado_entre_dos_huecos_es_un_tramo_de_un_solo_punto()
    {
        var serie = Serie("a", ("1", null), ("2", 5), ("3", null));
        var geometria = new GeometriaDeGrafica([serie], baseCero: true);

        geometria.Trazo(serie).ShouldStartWith("M");
        geometria.Trazo(serie).ShouldNotContain("L");
        geometria.PuntosConDato(serie).Count().ShouldBe(1);
    }

    [Fact]
    public void Los_periodos_de_todas_las_series_se_unen_en_orden()
    {
        var a = Serie("a", ("2024-03", 1), ("2024-01", 1));
        var b = Serie("b", ("2024-02", 2));

        new GeometriaDeGrafica([a, b], baseCero: true).Periodos.ShouldBe(["2024-01", "2024-02", "2024-03"]);
    }

    [Fact]
    public void Los_valores_mayores_quedan_mas_arriba_y_la_x_crece_con_el_tiempo()
    {
        var geometria = new GeometriaDeGrafica([Serie("a", ("1", 10), ("2", 20), ("3", 30))], baseCero: true);

        geometria.Y(30).ShouldBeLessThan(geometria.Y(10));
        geometria.X(0).ShouldBeLessThan(geometria.X(1));
        geometria.X(1).ShouldBeLessThan(geometria.X(2));
        geometria.X(0).ShouldBe(GeometriaDeGrafica.MargenIzquierdo, 0.001);
        geometria.X(2).ShouldBe(GeometriaDeGrafica.Ancho - GeometriaDeGrafica.MargenDerecho, 0.001);
        geometria.Y(0).ShouldBeLessThanOrEqualTo(GeometriaDeGrafica.Alto - GeometriaDeGrafica.MargenInferior + 0.001);
    }

    [Fact]
    public void Sin_ningun_dato_no_hay_grafica_y_no_falla()
    {
        var geometria = new GeometriaDeGrafica([Serie("a", ("1", null), ("2", null))], baseCero: true);

        geometria.HayDatos.ShouldBeFalse();
        geometria.Trazo(geometria.Series[0]).ShouldBeEmpty();
    }

    [Fact]
    public void Una_sola_serie_de_un_punto_se_centra()
    {
        var geometria = new GeometriaDeGrafica([Serie("a", ("1", 7))], baseCero: true);

        geometria.X(0).ShouldBe((GeometriaDeGrafica.MargenIzquierdo + GeometriaDeGrafica.Ancho - GeometriaDeGrafica.MargenDerecho) / 2, 0.001);
    }

    [Fact]
    public void Las_etiquetas_del_eje_x_son_pocas_aunque_haya_muchos_periodos()
    {
        var puntos = Enumerable.Range(0, 140).Select(i => ($"{2015 + (i / 12)}-{(i % 12) + 1:00}", (double?)i)).ToArray();
        var geometria = new GeometriaDeGrafica([Serie("a", puntos)], baseCero: true);

        geometria.EtiquetasX().Count.ShouldBeInRange(5, 9);
    }
}

public class ConstructorDeSeriesTests
{
    private static readonly Catalogos Catalogos = new(
        new MetadatosApi("datos-2026-08", ["INE"], []),
        [new TerritorioApi("region-murcia", "Región de Murcia", "region", "espana", "INE", ["demanda"]), new TerritorioApi("cartagena", "Cartagena", "punto_ine", "region-murcia", "INE", ["demanda"])],
        [new OpcionApi("hotel", "Hoteles", ["demanda"]), new OpcionApi("camping", "Campings", ["demanda"])],
        [new OpcionApi("espana", "Residentes en España", ["demanda"])],
        []);

    private static Dictionary<string, JsonElement> Fila(string periodo, string territorio, string tipo, string? residencia, string? valor, int? meses = null)
    {
        var json = $$"""{"periodo":"{{periodo}}","territorio":"{{territorio}}","tipo":"{{tipo}}"{{(residencia is null ? "" : $",\"residencia\":\"{residencia}\"")}}{{(meses is null ? "" : $",\"meses\":{meses}")}},"viajeros":{{valor ?? "null"}}}""";
        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;
    }

    private static RespuestaSerieApi Respuesta(params Dictionary<string, JsonElement>[] filas) => new([.. filas], new MetaSerieApi("datos-2026-08", null, filas.Length));

    [Fact]
    public void Solo_se_nombra_lo_que_distingue_a_unas_series_de_otras()
    {
        var respuesta = Respuesta(
            Fila("2024-01", "region-murcia", "hotel", "espana", "10"),
            Fila("2024-01", "cartagena", "hotel", "espana", "5"));

        var series = ConstructorDeSeries.Construir(respuesta, "viajeros", Catalogos);

        series.Select(s => s.Nombre).ShouldBe(["Región de Murcia", "Cartagena"]);
    }

    [Fact]
    public void Si_varian_varias_cosas_se_nombran_todas()
    {
        var respuesta = Respuesta(
            Fila("2024-01", "region-murcia", "hotel", "espana", "10"),
            Fila("2024-01", "region-murcia", "camping", "espana", "5"),
            Fila("2024-01", "cartagena", "hotel", "espana", "5"));

        ConstructorDeSeries.Construir(respuesta, "viajeros", Catalogos).Select(s => s.Nombre)
            .ShouldBe(["Región de Murcia · Hoteles", "Región de Murcia · Campings", "Cartagena · Hoteles"]);
    }

    [Fact]
    public void Una_sola_serie_se_llama_como_su_territorio()
    {
        var respuesta = Respuesta(Fila("2024-01", "region-murcia", "hotel", "espana", "10"));

        ConstructorDeSeries.Construir(respuesta, "viajeros", Catalogos).ShouldHaveSingleItem().Nombre.ShouldBe("Región de Murcia");
    }

    [Fact]
    public void Un_valor_null_de_la_api_es_un_hueco_y_nunca_un_cero()
    {
        var respuesta = Respuesta(Fila("2024-01", "region-murcia", "hotel", "espana", "10"), Fila("2024-02", "region-murcia", "hotel", "espana", null));

        var puntos = ConstructorDeSeries.Construir(respuesta, "viajeros", Catalogos).Single().Puntos;

        puntos[0].Valor.ShouldBe(10);
        puntos[1].Valor.ShouldBeNull();
    }

    [Theory]
    [InlineData("anio", 8, true)]
    [InlineData("anio", 12, false)]
    [InlineData("trimestre", 2, true)]
    [InlineData("trimestre", 3, false)]
    [InlineData("mes", 1, false)]
    public void Un_periodo_con_menos_meses_de_los_que_le_tocan_se_marca_como_incompleto(string agregacion, int meses, bool incompleto)
    {
        var respuesta = Respuesta(Fila("2026", "region-murcia", "hotel", "espana", "10", meses));

        ConstructorDeSeries.Construir(respuesta, "viajeros", Catalogos, agregacion).Single().Puntos.Single().Incompleto.ShouldBe(incompleto);
    }
}

public class ConstructorDeLlamadasTests
{
    private static readonly EstadoDeConsulta Estado = EstadoDeConsulta.Inicial("demanda") with { Desde = "2020-01", Hasta = "2020-12" };

    [Fact]
    public void Las_llamadas_equivalentes_apuntan_al_mismo_sitio_con_los_mismos_parametros()
    {
        const string url = "https://datos.example";
        const string consulta = "territorio=region-murcia&tipo=hotel&residencia=total&desde=2020-01&hasta=2020-12&agregacion=mes&medidas=pernoctaciones";

        ConstructorDeLlamadas.Curl(url, Estado).ShouldBe($"curl -s \"https://datos.example/v1/demanda?{consulta}\"");
        ConstructorDeLlamadas.CurlCsv(url + "/", Estado).ShouldBe($"curl -s -o demanda.csv \"https://datos.example/v1/demanda?{consulta}&formato=csv\"");
        ConstructorDeLlamadas.Python(url, Estado).ShouldContain($"pd.read_csv(\"https://datos.example/v1/demanda?{consulta}&formato=csv\")");
    }
}

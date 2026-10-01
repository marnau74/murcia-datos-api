using MurciaDatos.Datos.Consultas;
using MurciaDatos.Tests.Comunes;

using Shouldly;

namespace MurciaDatos.Datos.Tests;

public class ConsultasDeSeriesTests : IClassFixture<ReleaseCompartida>
{
    private readonly ReleaseCompartida _release;

    public ConsultasDeSeriesTests(ReleaseCompartida release) => _release = release;

    private static IReadOnlyList<Medida> Medidas(Hecho hecho, params string[] ids) =>
        [.. CatalogoDeMedidas.De(hecho).Where(m => ids.Contains(m.Id))];

    private ResultadoDeSerie Ejecutar(ConsultaDeSerie consulta, int maxFilas = 20_000) =>
        EjecutarCon(consulta, maxFilas, TestContext.Current.CancellationToken);

    private ResultadoDeSerie EjecutarCon(ConsultaDeSerie consulta, int maxFilas, CancellationToken cancelacion)
    {
        using var prestamo = _release.Instantanea.TryPrestar(out var p) ? p : throw new InvalidOperationException();
        return ConsultasDeSeries.Ejecutar(prestamo, consulta, maxFilas, cancelacion);
    }

    private static ConsultaDeSerie Demanda(
        string territorio = "region-murcia",
        string tipo = "hotel",
        string? residencia = "espana",
        Agregacion agregacion = Agregacion.Mes,
        int? desde = null,
        int? hasta = null,
        bool total = false,
        params string[] medidas) =>
        new(
            Hecho.Demanda,
            [territorio],
            [tipo],
            residencia is null ? [] : [residencia],
            total,
            desde,
            hasta,
            agregacion,
            Medidas(Hecho.Demanda, medidas.Length == 0 ? ["viajeros", "pernoctaciones"] : medidas),
            NumeroDeResidencias: 2);

    [Fact]
    public void Una_serie_mensual_devuelve_exactamente_las_cifras_de_los_datos()
    {
        var resultado = Ejecutar(Demanda(desde: 202401, hasta: 202403));

        resultado.Truncado.ShouldBeFalse();
        resultado.Filas.Select(f => f.Periodo).ShouldBe(["2024-01", "2024-02", "2024-03"]);
        foreach (var fila in resultado.Filas)
        {
            var mes = int.Parse(fila.Periodo[5..], System.Globalization.CultureInfo.InvariantCulture);
            var viajeros = ReleaseDePrueba.Viajeros("region-murcia", "hotel", "espana", 2024, mes)!.Value;
            fila.Valores[0].ShouldBe(viajeros);
            fila.Valores[1].ShouldBe(ReleaseDePrueba.Pernoctaciones("hotel", viajeros));
            fila.Territorio.ShouldBe("region-murcia");
            fila.Residencia.ShouldBe("espana");
            fila.Fuente.ShouldBe("INE");
            fila.Meses.ShouldBe(1);
        }
    }

    [Fact]
    public void Los_flujos_se_suman_al_agregar_por_anio()
    {
        var resultado = Ejecutar(Demanda(agregacion: Agregacion.Anio, desde: 201901, hasta: 201912, medidas: "viajeros"));

        var esperado = Enumerable.Range(1, 12).Sum(m => ReleaseDePrueba.Viajeros("region-murcia", "hotel", "espana", 2019, m)!.Value);
        var fila = resultado.Filas.ShouldHaveSingleItem();
        fila.Periodo.ShouldBe("2019");
        fila.Valores[0].ShouldBe(esperado);
        fila.Meses.ShouldBe(12);
    }

    [Fact]
    public void El_trimestre_agrupa_tres_meses()
    {
        var resultado = Ejecutar(Demanda(agregacion: Agregacion.Trimestre, desde: 201904, hasta: 201906, medidas: "viajeros"));

        var fila = resultado.Filas.ShouldHaveSingleItem();
        fila.Periodo.ShouldBe("2019-T2");
        fila.Valores[0].ShouldBe(Enumerable.Range(4, 3).Sum(m => ReleaseDePrueba.Viajeros("region-murcia", "hotel", "espana", 2019, m)!.Value));
    }

    [Fact]
    public void Un_anio_incompleto_dice_cuantos_meses_tiene_y_marca_los_provisionales()
    {
        var resultado = Ejecutar(Demanda(agregacion: Agregacion.Anio, desde: 202401, medidas: "viajeros"));

        var fila = resultado.Filas.ShouldHaveSingleItem();
        fila.Periodo.ShouldBe("2024");
        fila.Meses.ShouldBe(8);
        fila.Provisional.ShouldBeTrue();
    }

    [Fact]
    public void Un_mes_sin_dato_no_cuenta_como_cero_ni_como_mes_del_periodo()
    {
        // Abril a junio de 2020 no existen para los apartamentos de la región (residentes en España).
        var resultado = Ejecutar(Demanda(tipo: "apartamento", agregacion: Agregacion.Anio, desde: 202001, hasta: 202012, medidas: "viajeros"));

        var fila = resultado.Filas.ShouldHaveSingleItem();
        fila.Meses.ShouldBe(9);
        fila.Valores[0].ShouldBe(Enumerable.Range(1, 12).Sum(m => ReleaseDePrueba.Viajeros("region-murcia", "apartamento", "espana", 2020, m) ?? 0));

        var mensual = Ejecutar(Demanda(tipo: "apartamento", desde: 202004, hasta: 202006, medidas: "viajeros"));
        mensual.Filas.ShouldBeEmpty();
    }

    [Fact]
    public void Sin_filtrar_residencia_cada_residencia_es_una_fila()
    {
        var resultado = Ejecutar(Demanda(residencia: null, desde: 202401, hasta: 202401));

        resultado.Filas.Select(f => f.Residencia).ShouldBe(["espana", "extranjero"]);
    }

    [Fact]
    public void El_total_de_residencias_suma_ambas_y_se_llama_total()
    {
        var resultado = Ejecutar(Demanda(residencia: null, total: true, desde: 202401, hasta: 202401, medidas: "viajeros"));

        var fila = resultado.Filas.ShouldHaveSingleItem();
        fila.Residencia.ShouldBe("total");
        fila.Valores[0].ShouldBe(
            ReleaseDePrueba.Viajeros("region-murcia", "hotel", "espana", 2024, 1)!.Value
            + ReleaseDePrueba.Viajeros("region-murcia", "hotel", "extranjero", 2024, 1)!.Value);
    }

    [Fact]
    public void El_total_de_un_mes_al_que_le_falta_una_residencia_es_sin_dato_y_no_una_suma_parcial()
    {
        // En abril de 2020 solo hay residentes en el extranjero: sumarlos como «total» sería falso.
        var resultado = Ejecutar(Demanda(tipo: "apartamento", residencia: null, total: true, desde: 202003, hasta: 202007, medidas: "viajeros"));

        resultado.Filas.Select(f => f.Periodo).ShouldBe(["2020-03", "2020-04", "2020-05", "2020-06", "2020-07"]);
        resultado.Filas.Single(f => f.Periodo == "2020-04").Valores[0].ShouldBeNull();
        resultado.Filas.Single(f => f.Periodo == "2020-04").Meses.ShouldBe(0);
        resultado.Filas.Single(f => f.Periodo == "2020-03").Valores[0].ShouldNotBeNull();
    }

    [Fact]
    public void Las_existencias_y_tasas_se_promedian_y_los_nulos_siguen_siendo_nulos()
    {
        var consulta = new ConsultaDeSerie(
            Hecho.Oferta, ["region-murcia"], [], [], false, 202301, 202312, Agregacion.Anio,
            Medidas(Hecho.Oferta, "plazas", "ocupacion_plazas", "ocupacion_parcelas"));

        var filas = Ejecutar(consulta).Filas;

        var hotel = filas.Single(f => f.Tipo == "hotel");
        var plazasMedias = Enumerable.Range(1, 12).Average(m => Math.Round(20_000 + (100 * 5) + (m * 10.0)));
        Convert.ToDouble(hotel.Valores[0], System.Globalization.CultureInfo.InvariantCulture).ShouldBe(plazasMedias, 0.01);
        hotel.Valores[2].ShouldBeNull(); // un hotel no tiene parcelas: sin dato, no 0

        var camping = filas.Single(f => f.Tipo == "camping");
        camping.Valores[0].ShouldBeNull(); // el camping no informa de plazas
        camping.Valores[2].ShouldNotBeNull();
    }

    [Fact]
    public void Los_territorios_del_INE_y_de_murciaturistica_no_se_mezclan_en_una_fila()
    {
        var consulta = Demanda(residencia: "espana", desde: 202312, hasta: 202401) with { Territorios = ["region-murcia", "destino-la-manga"] };

        var filas = Ejecutar(consulta).Filas;

        filas.Where(f => f.Territorio == "destino-la-manga").ShouldAllBe(f => f.Fuente == "murciaturistica");
        filas.Where(f => f.Territorio == "region-murcia").ShouldAllBe(f => f.Fuente == "INE");
        filas.Any(f => f.Territorio == "destino-la-manga" && f.Periodo == "2024-01").ShouldBeFalse();
    }

    [Fact]
    public void Si_hay_mas_filas_que_el_limite_se_avisa_de_que_esta_truncado()
    {
        var resultado = Ejecutar(Demanda(residencia: null), maxFilas: 10);

        resultado.Filas.Count.ShouldBe(10);
        resultado.Truncado.ShouldBeTrue();
    }

    [Fact]
    public void Un_valor_con_forma_de_inyeccion_viaja_como_parametro_y_nunca_en_el_sql()
    {
        const string malicioso = "x'; DROP TABLE gold.dim_fecha; --";
        var consulta = Demanda() with { Territorios = [malicioso] };

        var (sql, parametros) = ConsultasDeSeries.Construir(consulta, 100);

        sql.ShouldNotContain("DROP");
        sql.ShouldNotContain("x'");
        parametros.ShouldContain(malicioso);

        // Y, ejecutada, no devuelve nada ni rompe nada.
        Ejecutar(consulta).Filas.ShouldBeEmpty();
        Ejecutar(Demanda(desde: 202401, hasta: 202401)).Filas.ShouldHaveSingleItem();
    }

    [Fact]
    public void Una_consulta_cancelada_se_interrumpe()
    {
        using var origen = new CancellationTokenSource();
        origen.Cancel();

        Should.Throw<OperationCanceledException>(() => EjecutarCon(Demanda(residencia: null), 20_000, origen.Token));
    }

    [Fact]
    public void Los_precios_traen_el_indice_y_la_variacion_interanual_con_su_nulo_del_primer_anio()
    {
        var consulta = new ConsultaDeSerie(
            Hecho.Precios, ["region-murcia"], ["hotel"], [], false, 201812, 201901, Agregacion.Mes,
            Medidas(Hecho.Precios, "indice_precios", "variacion_interanual"));

        var filas = Ejecutar(consulta).Filas;

        filas.Select(f => f.Periodo).ShouldBe(["2018-12", "2019-01"]);
        filas[0].Valores[1].ShouldBeNull();
        filas[1].Valores[1].ShouldBe(3.0);
        filas[0].Residencia.ShouldBeNull();
    }

    [Fact]
    public void Una_instantanea_retirada_sigue_sirviendo_a_quien_ya_la_tenia_prestada()
    {
        using var directorio = new DirectorioTemporal();
        var carpeta = directorio.Subcarpeta("r");
        var etiqueta = ReleaseDePrueba.Crear(carpeta);
        var instantanea = ReleaseCompartida.Abrir(carpeta, etiqueta);

        instantanea.TryPrestar(out var prestamo).ShouldBeTrue();
        instantanea.Retirar();

        instantanea.TryPrestar(out _).ShouldBeFalse(); // ya no admite nuevos usos…
        using (prestamo)
        {
            // …pero el préstamo anterior todavía puede consultar.
            var resultado = ConsultasDeSeries.Ejecutar(prestamo, Demanda(desde: 202401, hasta: 202401), 100, TestContext.Current.CancellationToken);
            resultado.Filas.ShouldHaveSingleItem();
        }
    }

    [Fact]
    public async Task El_almacen_cambia_de_version_sin_cortar_las_consultas_en_curso()
    {
        using var directorio = new DirectorioTemporal();
        var carpetaA = directorio.Subcarpeta("a");
        var carpetaB = directorio.Subcarpeta("b");
        var etiquetaA = ReleaseDePrueba.Crear(carpetaA);
        var etiquetaB = ReleaseDePrueba.Crear(carpetaB, new VarianteDeRelease { Escala = 2 });
        using var almacen = new AlmacenDeInstantaneas();
        almacen.Reemplazar(ReleaseCompartida.Abrir(carpetaA, etiquetaA));

        var consulta = Demanda(desde: 202401, hasta: 202401, medidas: "viajeros");
        var cancelacionDeLaPrueba = TestContext.Current.CancellationToken;
        var lectores = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            var valores = new List<long>();
            for (var i = 0; i < 200; i++)
            {
                using var prestamo = almacen.Prestar() ?? throw new InvalidOperationException("sin datos");
                valores.Add(Convert.ToInt64(ConsultasDeSeries.Ejecutar(prestamo, consulta, 10, cancelacionDeLaPrueba).Filas.Single().Valores[0], System.Globalization.CultureInfo.InvariantCulture));
            }

            return valores;
        })).ToList();

        await Task.Delay(20, TestContext.Current.CancellationToken);
        almacen.Reemplazar(ReleaseCompartida.Abrir(carpetaB, etiquetaB));

        var unaVez = ReleaseDePrueba.Viajeros("region-murcia", "hotel", "espana", 2024, 1)!.Value;
        var todos = (await Task.WhenAll(lectores)).SelectMany(v => v).ToList();

        // Cada consulta devolvió la cifra de una versión entera (A o B), nunca un error ni una mezcla.
        todos.ShouldAllBe(v => v == unaVez || v == unaVez * 2);
    }
}

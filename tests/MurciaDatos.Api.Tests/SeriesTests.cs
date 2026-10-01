using System.Net;
using System.Text;
using System.Text.Json;

using MurciaDatos.Tests.Comunes;

using Shouldly;

namespace MurciaDatos.Api.Tests;

public class SeriesTests(ApiCompartida compartida) : IClassFixture<ApiCompartida>
{
    private readonly HttpClient _cliente = compartida.Cliente;

    private static long Viajeros(string territorio, string tipo, string residencia, int anio, int mes) =>
        ReleaseDePrueba.Viajeros(territorio, tipo, residencia, anio, mes)!.Value;

    [Fact]
    public async Task La_demanda_devuelve_las_cifras_exactas_con_su_envoltorio()
    {
        using var respuesta = await _cliente.GetAsync("/v1/demanda?territorio=region-murcia&tipo=hotel&residencia=espana&desde=2024-01&hasta=2024-02", TestContext.Current.CancellationToken);
        var json = await respuesta.JsonAsync();

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);
        var datos = json.GetProperty("datos").EnumerateArray().ToList();
        datos.Count.ShouldBe(2);
        datos[0].GetProperty("periodo").GetString().ShouldBe("2024-01");
        datos[0].GetProperty("viajeros").GetInt64().ShouldBe(Viajeros("region-murcia", "hotel", "espana", 2024, 1));
        datos[0].GetProperty("pernoctaciones").GetInt64().ShouldBe(Viajeros("region-murcia", "hotel", "espana", 2024, 1) * 3);
        datos[0].GetProperty("residencia").GetString().ShouldBe("espana");
        datos[0].GetProperty("fuente").GetString().ShouldBe("INE");
        datos[0].GetProperty("provisional").GetBoolean().ShouldBeFalse();
        datos[0].TryGetProperty("meses", out _).ShouldBeFalse(); // solo al agregar

        var meta = json.GetProperty("meta");
        meta.GetProperty("version_datos").GetString().ShouldBe("datos-2024-08");
        meta.GetProperty("provisional_desde").GetString().ShouldBe("2024-06");
        meta.GetProperty("agregacion").GetString().ShouldBe("mes");
        meta.GetProperty("total_filas").GetInt32().ShouldBe(2);
        meta.GetProperty("medidas").EnumerateArray().Select(m => m.GetProperty("id").GetString()).ShouldBe(["viajeros", "pernoctaciones"]);
    }

    [Fact]
    public async Task Los_meses_provisionales_se_marcan()
    {
        var json = await (await _cliente.GetAsync("/v1/demanda?territorio=region-murcia&tipo=hotel&residencia=espana&desde=2024-05&hasta=2024-06&medidas=viajeros", TestContext.Current.CancellationToken)).JsonAsync();

        json.GetProperty("datos").EnumerateArray().Select(d => d.GetProperty("provisional").GetBoolean()).ShouldBe([false, true]);
    }

    [Fact]
    public async Task Las_medidas_salen_en_el_orden_pedido()
    {
        var json = await (await _cliente.GetAsync("/v1/demanda?territorio=region-murcia&tipo=hotel&residencia=espana&desde=2024-01&hasta=2024-01&medidas=pernoctaciones,viajeros", TestContext.Current.CancellationToken)).JsonAsync();

        json.GetProperty("datos")[0].EnumerateObject().Select(p => p.Name).ShouldBe(["periodo", "territorio", "tipo", "residencia", "fuente", "provisional", "pernoctaciones", "viajeros"]);
    }

    [Fact]
    public async Task Agregar_por_anio_suma_los_flujos_y_cuenta_los_meses()
    {
        var json = await (await _cliente.GetAsync("/v1/demanda?territorio=region-murcia&tipo=hotel&residencia=total&agregacion=anio&desde=2023-01&medidas=viajeros", TestContext.Current.CancellationToken)).JsonAsync();

        var filas = json.GetProperty("datos").EnumerateArray().ToList();
        filas.Select(f => f.GetProperty("periodo").GetString()).ShouldBe(["2023", "2024"]);
        filas[0].GetProperty("meses").GetInt32().ShouldBe(12);
        filas[1].GetProperty("meses").GetInt32().ShouldBe(8);
        filas[0].GetProperty("residencia").GetString().ShouldBe("total");
        filas[0].GetProperty("viajeros").GetInt64().ShouldBe(
            Enumerable.Range(1, 12).Sum(m => Viajeros("region-murcia", "hotel", "espana", 2023, m) + Viajeros("region-murcia", "hotel", "extranjero", 2023, m)));
        filas[1].GetProperty("provisional").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task Un_dato_que_no_existe_es_null_y_no_cero()
    {
        // Oferta de un hotel: no tiene parcelas, y el camping no informa de plazas.
        var json = await (await _cliente.GetAsync("/v1/oferta?territorio=region-murcia&desde=2024-01&hasta=2024-01&medidas=plazas,parcelas", TestContext.Current.CancellationToken)).JsonAsync();

        var filas = json.GetProperty("datos").EnumerateArray().ToList();
        var hotel = filas.Single(f => f.GetProperty("tipo").GetString() == "hotel");
        var camping = filas.Single(f => f.GetProperty("tipo").GetString() == "camping");
        hotel.GetProperty("parcelas").ValueKind.ShouldBe(JsonValueKind.Null);
        hotel.GetProperty("plazas").ValueKind.ShouldBe(JsonValueKind.Number);
        camping.GetProperty("plazas").ValueKind.ShouldBe(JsonValueKind.Null);
        camping.GetProperty("parcelas").GetInt64().ShouldBe(1500);
    }

    [Fact]
    public async Task Los_precios_traen_la_variacion_solo_en_mensual()
    {
        var mensual = await (await _cliente.GetAsync("/v1/precios?territorio=region-murcia&desde=2019-01&hasta=2019-01", TestContext.Current.CancellationToken)).JsonAsync();
        var fila = mensual.GetProperty("datos")[0];
        fila.GetProperty("indice_precios").GetDouble().ShouldBe(100 + 3 + 1);
        fila.GetProperty("variacion_interanual").GetDouble().ShouldBe(3.0);
        fila.TryGetProperty("residencia", out _).ShouldBeFalse(); // los precios no distinguen residencia

        // Sin pedir medidas con agregación anual, la variación interanual no aparece (no se puede promediar).
        var anual = await (await _cliente.GetAsync("/v1/precios?territorio=region-murcia&agregacion=anio&desde=2019-01&hasta=2019-12", TestContext.Current.CancellationToken)).JsonAsync();
        anual.GetProperty("datos")[0].TryGetProperty("variacion_interanual", out _).ShouldBeFalse();
        anual.GetProperty("meta").GetProperty("medidas").EnumerateArray().Select(m => m.GetProperty("id").GetString()).ShouldBe(["indice_precios"]);

        using var pedida = await _cliente.GetAsync("/v1/precios?agregacion=anio&medidas=variacion_interanual", TestContext.Current.CancellationToken);
        pedida.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await pedida.JsonAsync()).GetProperty("errors").GetProperty("medidas")[0].GetString()!.ShouldContain("solo está disponible con agregacion=mes");
    }

    [Fact]
    public async Task Los_listados_de_un_filtro_aceptan_cualquier_orden_y_mayusculas()
    {
        var a = await (await _cliente.GetAsync("/v1/demanda?territorio=costa-calida,cartagena&tipo=hotel&residencia=espana&desde=2024-01&hasta=2024-01&medidas=viajeros", TestContext.Current.CancellationToken)).JsonAsync();
        var b = await (await _cliente.GetAsync("/v1/demanda?TERRITORIO=Cartagena,COSTA-CALIDA&tipo=HOTEL&residencia=espana&desde=2024-01&hasta=2024-01&medidas=viajeros", TestContext.Current.CancellationToken)).JsonAsync();

        a.GetProperty("datos").EnumerateArray().Select(d => d.GetProperty("territorio").GetString()).ShouldBe(["cartagena", "costa-calida"]);
        b.GetRawText().ShouldBe(a.GetRawText());
    }

    [Theory]
    [InlineData("territorio=atlantida", "territorio", "Valor desconocido «atlantida». Permitidos:")]
    [InlineData("tipo=castillo", "tipo", "Permitidos: apartamento, hotel")]
    [InlineData("residencia=marte", "residencia", "Permitidos: espana, extranjero, total")]
    [InlineData("residencia=total,espana", "residencia", "«total» no se puede combinar")]
    [InlineData("agregacion=semana", "agregacion", "Permitidos: mes, trimestre, anio")]
    [InlineData("medidas=beneficio", "medidas", "Permitidos: pernoctaciones, viajeros")]
    [InlineData("formato=xml", "formato", "Permitidos: json, csv")]
    [InlineData("desde=2024-13", "desde", "AAAA-MM")]
    [InlineData("hasta=hoy", "hasta", "AAAA-MM")]
    [InlineData("desde=2024-05&hasta=2024-01", "desde", "no puede ser posterior")]
    [InlineData("excel=true", "excel", "solo tiene sentido con formato=csv")]
    [InlineData("excel=quizas&formato=csv", "excel", "Permitidos: true, false")]
    [InlineData("territorio=", "territorio", "No puede estar vacío")]
    [InlineData("parametro=1", "parametro", "Parámetro desconocido")]
    public async Task Un_parametro_no_valido_da_400_con_el_error_por_parametro_y_los_valores_permitidos(string consulta, string parametro, string mensaje)
    {
        using var respuesta = await _cliente.GetAsync("/v1/demanda?" + consulta, TestContext.Current.CancellationToken);
        var json = await respuesta.JsonAsync();

        respuesta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        respuesta.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        json.GetProperty("title").GetString().ShouldBe("Parámetros no válidos");
        json.GetProperty("errors").GetProperty(parametro).EnumerateArray().Select(e => e.GetString()).ShouldContain(e => e!.Contains(mensaje, StringComparison.Ordinal));
    }

    [Fact]
    public async Task La_oferta_y_los_precios_no_admiten_el_parametro_residencia()
    {
        using var respuesta = await _cliente.GetAsync("/v1/oferta?residencia=espana", TestContext.Current.CancellationToken);

        respuesta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await respuesta.JsonAsync()).GetProperty("errors").TryGetProperty("residencia", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Los_filtros_solo_aceptan_valores_que_hay_en_cada_recurso()
    {
        // España tiene precios pero no demanda: pedirla para la demanda es un error, y la lista permitida lo explica.
        using var demanda = await _cliente.GetAsync("/v1/demanda?territorio=espana", TestContext.Current.CancellationToken);
        using var precios = await _cliente.GetAsync("/v1/precios?territorio=espana&desde=2024-01&hasta=2024-01", TestContext.Current.CancellationToken);

        demanda.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        precios.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("x'; DROP TABLE gold.fct_demanda_mensual; --")]
    [InlineData("%27%20OR%201%3D1%20--")]
    [InlineData("region-murcia' UNION SELECT * FROM gold.dim_fecha --")]
    public async Task Los_intentos_de_inyeccion_dan_400_y_no_tocan_los_datos(string valor)
    {
        using var respuesta = await _cliente.GetAsync("/v1/demanda?territorio=" + Uri.EscapeDataString(valor), TestContext.Current.CancellationToken);
        using var intacta = await _cliente.GetAsync("/v1/demanda?territorio=region-murcia&tipo=hotel&residencia=espana&desde=2024-01&hasta=2024-01", TestContext.Current.CancellationToken);

        respuesta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        intacta.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await intacta.JsonAsync()).GetProperty("datos").GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Un_parametro_demasiado_largo_o_con_demasiados_valores_se_rechaza()
    {
        using var largo = await _cliente.GetAsync("/v1/demanda?territorio=" + new string('a', 1000), TestContext.Current.CancellationToken);
        using var muchos = await _cliente.GetAsync("/v1/demanda?territorio=" + string.Join(",", Enumerable.Range(0, 60).Select(i => "t" + i)), TestContext.Current.CancellationToken);

        largo.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        muchos.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await largo.JsonAsync()).GetProperty("errors").GetProperty("territorio")[0].GetString()!.ShouldContain("Demasiado largo");
    }

    // ---- CSV ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task El_csv_se_pide_con_el_parametro_formato()
    {
        using var respuesta = await _cliente.GetAsync("/v1/demanda?territorio=region-murcia&tipo=hotel&residencia=espana&desde=2024-01&hasta=2024-02&medidas=viajeros&formato=csv", TestContext.Current.CancellationToken);
        var texto = await respuesta.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        respuesta.Content.Headers.ContentType!.MediaType.ShouldBe("text/csv");
        respuesta.Content.Headers.ContentDisposition!.FileName.ShouldBe("demanda.csv");
        var lineas = texto.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        lineas[0].ShouldBe("periodo,territorio,tipo,residencia,fuente,provisional,viajeros");
        lineas[1].ShouldBe($"2024-01,region-murcia,hotel,espana,INE,false,{Viajeros("region-murcia", "hotel", "espana", 2024, 1)}");
        lineas.Length.ShouldBe(3);
    }

    [Fact]
    public async Task El_csv_se_pide_tambien_con_la_cabecera_accept()
    {
        using var respuesta = await _cliente.PedirAsync("/v1/precios?desde=2024-01&hasta=2024-01&territorio=region-murcia", ("Accept", "text/csv"));

        respuesta.Content.Headers.ContentType!.MediaType.ShouldBe("text/csv");
        (await respuesta.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldStartWith("periodo,territorio,tipo,fuente,provisional,indice_precios,variacion_interanual");
        respuesta.Una("Vary").ShouldContain("Accept");
    }

    [Fact]
    public async Task Pedir_json_y_csv_en_accept_da_json_y_el_parametro_manda_sobre_la_cabecera()
    {
        using var ambos = await _cliente.PedirAsync("/v1/precios?desde=2024-01&hasta=2024-01&territorio=region-murcia", ("Accept", "application/json, text/csv"));
        using var parametro = await _cliente.PedirAsync("/v1/precios?desde=2024-01&hasta=2024-01&territorio=region-murcia&formato=json", ("Accept", "text/csv"));

        ambos.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");
        parametro.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");
    }

    [Fact]
    public async Task El_csv_para_excel_usa_punto_y_coma_decimales_con_coma_y_bom()
    {
        using var respuesta = await _cliente.GetAsync("/v1/precios?territorio=region-murcia&desde=2019-01&hasta=2019-01&medidas=variacion_interanual&formato=csv&excel=true", TestContext.Current.CancellationToken);
        var bytes = await respuesta.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        var texto = Encoding.UTF8.GetString(bytes);

        bytes.Take(3).ShouldBe([0xEF, 0xBB, 0xBF]);
        texto.TrimStart('﻿').ShouldBe("periodo;territorio;tipo;fuente;provisional;variacion_interanual\r\n2019-01;region-murcia;hotel;INE;false;3\r\n");

        // Un decimal con parte fraccionaria usa la coma.
        using var oferta = await _cliente.GetAsync("/v1/oferta?territorio=region-murcia&tipo=hotel&desde=2024-01&hasta=2024-01&medidas=ocupacion_plazas&formato=csv&excel=true", TestContext.Current.CancellationToken);
        (await oferta.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain(";55,5");
    }

    [Fact]
    public async Task Los_errores_de_un_csv_siguen_siendo_problem_json()
    {
        using var respuesta = await _cliente.GetAsync("/v1/demanda?tipo=castillo&formato=csv", TestContext.Current.CancellationToken);

        respuesta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        respuesta.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
    }

    // ---- Límites -----------------------------------------------------------------------------------------

    [Fact]
    public async Task Una_consulta_con_mas_filas_que_el_limite_se_rechaza_y_dice_como_acotarla()
    {
        using var api = new ApiDePrueba(new Dictionary<string, string?> { ["Datos:MaxFilas"] = "5" });
        await api.EsperarDatosAsync();
        using var cliente = api.CreateClient();

        using var respuesta = await cliente.GetAsync("/v1/demanda?territorio=region-murcia", TestContext.Current.CancellationToken);
        using var acotada = await cliente.GetAsync("/v1/demanda?territorio=region-murcia&tipo=hotel&residencia=total&desde=2024-01&hasta=2024-03", TestContext.Current.CancellationToken);

        respuesta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await respuesta.JsonAsync()).GetProperty("detail").GetString()!.ShouldContain("máximo por respuesta es 5 filas");
        acotada.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}

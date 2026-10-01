using System.Net;
using System.Text.Json;

using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

using MurciaDatos.Api.Salud;
using MurciaDatos.Datos.Actualizacion;
using MurciaDatos.Datos.Consultas;
using MurciaDatos.Datos.Origen;
using MurciaDatos.Tests.Comunes;

using Shouldly;

namespace MurciaDatos.Api.Tests;

public class IndicadoresTests(ApiCompartida compartida) : IClassFixture<ApiCompartida>
{
    private readonly HttpClient _cliente = compartida.Cliente;

    [Fact]
    public async Task La_estacionalidad_recupera_el_perfil_con_el_que_se_generaron_los_datos()
    {
        var json = await (await _cliente.GetAsync("/v1/indicadores/estacionalidad?territorio=region-murcia&tipo=hotel", TestContext.Current.CancellationToken)).JsonAsync();

        var datos = json.GetProperty("datos");
        datos.GetProperty("residencia").GetString().ShouldBe("total");
        datos.GetProperty("medida").GetString().ShouldBe("pernoctaciones");
        datos.GetProperty("anios_usados").EnumerateArray().Select(a => a.GetInt32()).ShouldBe([2018, 2019, 2020, 2021, 2022, 2023]); // 2024 está incompleto
        var perfil = datos.GetProperty("perfil").EnumerateArray().ToList();
        for (var mes = 1; mes <= 12; mes++)
        {
            perfil[mes - 1].GetProperty("indice").GetDouble().ShouldBe(ReleaseDePrueba.Estacionalidad[mes - 1] * 100, 0.1);
        }

        perfil[0].GetProperty("nombre_mes").GetString().ShouldBe("enero");
        datos.GetProperty("relacion_agosto_enero").GetDouble().ShouldBe(Math.Round(1.9 / 0.55, 2));
    }

    [Fact]
    public async Task Un_territorio_sin_anios_completos_devuelve_el_perfil_vacio_y_no_inventa()
    {
        // La Manga solo tiene datos hasta diciembre de 2023 pero sí años completos; los apartamentos de la región tienen un hueco en 2020.
        var apartamentos = await (await _cliente.GetAsync("/v1/indicadores/estacionalidad?territorio=region-murcia&tipo=apartamento&residencia=espana", TestContext.Current.CancellationToken)).JsonAsync();

        apartamentos.GetProperty("datos").GetProperty("anios_usados").EnumerateArray().Select(a => a.GetInt32()).ShouldNotContain(2020);
    }

    [Fact]
    public async Task La_variacion_compara_con_el_anio_anterior_y_con_2019()
    {
        var json = await (await _cliente.GetAsync("/v1/indicadores/variacion?territorio=region-murcia&tipo=hotel&medida=viajeros&residencia=espana", TestContext.Current.CancellationToken)).JsonAsync();

        var datos = json.GetProperty("datos");
        datos.GetProperty("anio").GetInt32().ShouldBe(2024);
        datos.GetProperty("ultimo_mes").GetInt32().ShouldBe(8);
        datos.GetProperty("provisional").GetBoolean().ShouldBeTrue();

        static long V(int anio, int mes) => ReleaseDePrueba.Viajeros("region-murcia", "hotel", "espana", anio, mes)!.Value;
        datos.GetProperty("mes").GetProperty("valor").GetInt64().ShouldBe(V(2024, 8));
        datos.GetProperty("mes").GetProperty("frente_anio_anterior").GetProperty("variacion_pct").GetDouble().ShouldBe(Math.Round((((double)V(2024, 8) / V(2023, 8)) - 1) * 100, 2));
        datos.GetProperty("acumulado").GetProperty("frente_2019").GetProperty("valor_referencia").GetInt64().ShouldBe(Enumerable.Range(1, 8).Sum(m => V(2019, m)));
    }

    [Fact]
    public async Task La_variacion_de_un_anio_concreto_y_con_hueco_en_la_referencia_no_se_calcula()
    {
        // Abril-junio de 2020 no existen para los apartamentos (España): el acumulado de 2021 frente a 2020 queda sin variación.
        var json = await (await _cliente.GetAsync("/v1/indicadores/variacion?territorio=region-murcia&tipo=apartamento&residencia=espana&anio=2021", TestContext.Current.CancellationToken)).JsonAsync();

        var datos = json.GetProperty("datos");
        datos.GetProperty("ultimo_mes").GetInt32().ShouldBe(12);
        datos.GetProperty("acumulado").GetProperty("frente_anio_anterior").GetProperty("variacion_pct").ValueKind.ShouldBe(JsonValueKind.Null);
        datos.GetProperty("acumulado").GetProperty("frente_2019").GetProperty("variacion_pct").ValueKind.ShouldBe(JsonValueKind.Number);
        datos.GetProperty("mes").GetProperty("frente_anio_anterior").GetProperty("variacion_pct").ValueKind.ShouldBe(JsonValueKind.Number); // diciembre de 2020 sí existe
    }

    [Fact]
    public async Task Un_anio_sin_datos_devuelve_todo_null()
    {
        var json = await (await _cliente.GetAsync("/v1/indicadores/variacion?anio=2010", TestContext.Current.CancellationToken)).JsonAsync();

        var datos = json.GetProperty("datos");
        datos.GetProperty("ultimo_mes").ValueKind.ShouldBe(JsonValueKind.Null);
        datos.GetProperty("mes").GetProperty("valor").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Theory]
    [InlineData("/v1/indicadores/estacionalidad?territorio=cartagena,costa-calida", "territorio", "Solo admite un valor")]
    [InlineData("/v1/indicadores/estacionalidad?territorio=espana", "territorio", "Permitidos")]
    [InlineData("/v1/indicadores/estacionalidad?medida=beneficio", "medida", "Permitidos: pernoctaciones, viajeros")]
    [InlineData("/v1/indicadores/estacionalidad?anio=2024", "anio", "Parámetro desconocido")]
    [InlineData("/v1/indicadores/variacion?incluir_provisionales=true", "incluir_provisionales", "Parámetro desconocido")]
    [InlineData("/v1/indicadores/variacion?anio=dosmil", "anio", "no es un año válido")]
    [InlineData("/v1/indicadores/estacionalidad?incluir_provisionales=tal vez", "incluir_provisionales", "Permitidos: true, false")]
    public async Task Los_parametros_de_los_indicadores_se_validan_igual_que_los_de_las_series(string ruta, string parametro, string mensaje)
    {
        using var respuesta = await _cliente.GetAsync(ruta, TestContext.Current.CancellationToken);

        respuesta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await respuesta.JsonAsync()).GetProperty("errors").GetProperty(parametro).EnumerateArray().Select(e => e.GetString()).ShouldContain(e => e!.Contains(mensaje, StringComparison.Ordinal));
    }
}

public class SaludYActualizacionTests
{
    [Fact]
    public async Task Con_datos_cargados_la_sonda_de_listo_responde_200_y_dice_la_version()
    {
        using var api = new ApiDePrueba();
        await api.EsperarDatosAsync();
        using var cliente = api.CreateClient();

        using var respuesta = await cliente.GetAsync("/health/ready", TestContext.Current.CancellationToken);
        var json = await respuesta.JsonAsync();

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);
        json.GetProperty("estado").GetString().ShouldBe("lista");
        json.GetProperty("comprobaciones").GetProperty("datos").GetProperty("datos").GetProperty("version_datos").GetString().ShouldBe("datos-2024-08");
    }

    [Fact]
    public async Task Sin_datos_la_sonda_de_listo_responde_503_pero_la_de_viva_sigue_en_200_y_la_api_pide_reintentar()
    {
        using var api = new ApiDePrueba(sinDatos: true);
        using var cliente = api.CreateClient();
        await Task.Delay(500, TestContext.Current.CancellationToken); // deja correr el primer intento del actualizador

        using var lista = await cliente.GetAsync("/health/ready", TestContext.Current.CancellationToken);
        using var viva = await cliente.GetAsync("/health/live", TestContext.Current.CancellationToken);
        using var datos = await cliente.GetAsync("/v1/demanda", TestContext.Current.CancellationToken);

        lista.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await lista.JsonAsync()).GetProperty("estado").GetString().ShouldBe("no_lista");
        viva.StatusCode.ShouldBe(HttpStatusCode.OK);
        datos.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        datos.Una("Retry-After").ShouldBe("30");
        (await datos.JsonAsync()).GetProperty("title").GetString().ShouldBe("Datos no disponibles");
        datos.Headers.ETag.ShouldBeNull();
    }

    [Fact]
    public async Task Cuando_los_datos_aparecen_la_api_empieza_a_servirlos_sin_reiniciar()
    {
        using var api = new ApiDePrueba(sinDatos: true);
        using var cliente = api.CreateClient();
        await Task.Delay(300, TestContext.Current.CancellationToken);
        (await cliente.GetAsync("/v1/territorios", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);

        ReleaseDePrueba.Crear(api.CarpetaDeRelease);
        (await api.Actualizacion.ActualizarAsync(TestContext.Current.CancellationToken)).ShouldBe(ResultadoDeActualizacion.Actualizado);

        (await cliente.GetAsync("/v1/territorios", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Si_hace_dos_intervalos_que_no_se_comprueba_el_origen_la_salud_sale_degradada()
    {
        // El actualizador se ha parado o no termina nunca, pero sin dejar ningún error: sin esta comprobación la sonda
        // seguiría diciendo «lista» mientras los datos envejecen.
        using var directorio = new DirectorioTemporal();
        var opciones = Options.Create(new OpcionesDeDatos
        {
            Origen = TipoDeOrigen.Directorio,
            Ruta = directorio.Subcarpeta("release"),
            Directorio = directorio.Subcarpeta("datos"),
            IntervaloHoras = 6,
        });
        ReleaseDePrueba.Crear(opciones.Value.Ruta!);
        var reloj = new FakeTimeProvider(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
        using var almacen = new AlmacenDeInstantaneas();
        using var servicio = new ServicioDeActualizacion(new OrigenDirectorio(opciones), almacen, [], opciones, reloj, NullLogger<ServicioDeActualizacion>.Instance);
        var salud = new SaludDeLosDatos(almacen, servicio, opciones, reloj);
        await servicio.ActualizarAsync(TestContext.Current.CancellationToken);

        reloj.Advance(TimeSpan.FromHours(12));
        (await salud.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken)).Status.ShouldBe(HealthStatus.Healthy);

        reloj.Advance(TimeSpan.FromMinutes(1));
        var pasada = await salud.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);
        pasada.Status.ShouldBe(HealthStatus.Degraded);
        pasada.Description!.ShouldContain("no se comprueba");
    }

    [Fact]
    public async Task Una_release_rota_deja_la_api_degradada_pero_sirviendo_la_anterior_y_se_ve_en_metadatos()
    {
        using var api = new ApiDePrueba();
        await api.EsperarDatosAsync();
        using var cliente = api.CreateClient();

        ReleaseDePrueba.Crear(api.CarpetaDeRelease, new VarianteDeRelease { SinColumnaViajeros = true, Hasta = "2024-09" });
        (await api.Actualizacion.ActualizarAsync(TestContext.Current.CancellationToken)).ShouldBe(ResultadoDeActualizacion.Rechazado);

        using var lista = await cliente.GetAsync("/health/ready", TestContext.Current.CancellationToken);
        var metadatos = await (await cliente.GetAsync("/v1/metadatos", TestContext.Current.CancellationToken)).JsonAsync();
        using var demanda = await cliente.GetAsync("/v1/demanda?territorio=region-murcia&tipo=hotel&residencia=espana&desde=2024-01&hasta=2024-01", TestContext.Current.CancellationToken);

        lista.StatusCode.ShouldBe(HttpStatusCode.OK); // sigue sirviendo: no se saca de rotación
        (await lista.JsonAsync()).GetProperty("estado").GetString().ShouldBe("degradada");
        metadatos.GetProperty("version_datos").GetString().ShouldBe("datos-2024-08");
        metadatos.GetProperty("problema_actualizacion").GetString()!.ShouldContain("viajeros");
        demanda.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Una_release_con_una_version_mayor_incompatible_del_contrato_no_se_carga_nunca()
    {
        using var api = new ApiDePrueba(sinDatos: true);
        ReleaseDePrueba.Crear(api.CarpetaDeRelease, new VarianteDeRelease { VersionContrato = "2.0.0" });
        using var cliente = api.CreateClient();
        await Task.Delay(500, TestContext.Current.CancellationToken);

        using var datos = await cliente.GetAsync("/v1/territorios", TestContext.Current.CancellationToken);

        datos.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }
}

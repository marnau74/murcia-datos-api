using System.Net;

using Shouldly;

namespace MurciaDatos.Api.Tests;

public class LimiteDePeticionesTests
{
    private static ApiDePrueba Crear(int limite = 3, int ventana = 60, bool proxy = false) =>
        new(
            new Dictionary<string, string?>
            {
                ["Limites:PeticionesPorVentana"] = limite.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["Limites:VentanaSegundos"] = ventana.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["Proxy:ConfiarEnCabecerasReenviadas"] = proxy ? "true" : "false",
            },
            relojFalso: true);

    [Fact]
    public async Task Al_pasarse_del_limite_responde_429_con_retry_after_y_problem_json()
    {
        using var api = Crear(limite: 3);
        await api.EsperarDatosAsync();
        using var cliente = api.CreateClient();

        var restantes = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            using var ok = await cliente.GetAsync("/v1/territorios", TestContext.Current.CancellationToken);
            ok.StatusCode.ShouldBe(HttpStatusCode.OK);
            ok.Una("RateLimit-Limit").ShouldBe("3");
            ok.Una("RateLimit-Policy").ShouldBe("3;w=60");
            restantes.Add(ok.Una("RateLimit-Remaining"));
        }

        restantes.ShouldBe(["2", "1", "0"]);

        using var limitada = await cliente.GetAsync("/v1/territorios", TestContext.Current.CancellationToken);
        limitada.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        limitada.Una("Retry-After").ShouldBe("60");
        limitada.Una("RateLimit-Remaining").ShouldBe("0");
        limitada.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        (await limitada.JsonAsync()).GetProperty("title").GetString().ShouldBe("Demasiadas peticiones");
    }

    [Fact]
    public async Task Retry_after_baja_con_el_tiempo_y_la_ventana_se_reinicia()
    {
        using var api = Crear(limite: 1, ventana: 60);
        await api.EsperarDatosAsync();
        using var cliente = api.CreateClient();

        (await cliente.GetAsync("/v1/territorios", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        api.Reloj!.Advance(TimeSpan.FromSeconds(45));

        using var limitada = await cliente.GetAsync("/v1/territorios", TestContext.Current.CancellationToken);
        limitada.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        limitada.Una("Retry-After").ShouldBe("15");

        api.Reloj.Advance(TimeSpan.FromSeconds(15));
        (await cliente.GetAsync("/v1/territorios", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Las_respuestas_de_la_cache_y_los_304_tambien_cuentan()
    {
        using var api = Crear(limite: 2);
        await api.EsperarDatosAsync();
        using var cliente = api.CreateClient();

        using var primera = await cliente.GetAsync("/v1/territorios", TestContext.Current.CancellationToken);
        using var conditional = await cliente.PedirAsync("/v1/territorios", ("If-None-Match", primera.Headers.ETag!.ToString()));
        using var tercera = await cliente.GetAsync("/v1/territorios", TestContext.Current.CancellationToken);

        conditional.StatusCode.ShouldBe(HttpStatusCode.NotModified);
        tercera.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Las_sondas_de_salud_y_el_explorador_no_se_limitan()
    {
        using var api = Crear(limite: 1);
        await api.EsperarDatosAsync();
        using var cliente = api.CreateClient();

        for (var i = 0; i < 5; i++)
        {
            (await cliente.GetAsync("/health/live", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
            (await cliente.GetAsync("/", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task Detras_de_un_proxy_de_confianza_cada_ip_real_tiene_su_propio_limite()
    {
        using var api = Crear(limite: 1, proxy: true);
        await api.EsperarDatosAsync();
        using var cliente = api.CreateClient();

        (await cliente.PedirAsync("/v1/territorios", ("X-Forwarded-For", "203.0.113.7"))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await cliente.PedirAsync("/v1/territorios", ("X-Forwarded-For", "203.0.113.7"))).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await cliente.PedirAsync("/v1/territorios", ("X-Forwarded-For", "198.51.100.9"))).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Sin_proxy_configurado_la_cabecera_x_forwarded_for_no_sirve_para_saltarse_el_limite()
    {
        using var api = Crear(limite: 1, proxy: false);
        await api.EsperarDatosAsync();
        using var cliente = api.CreateClient();

        (await cliente.PedirAsync("/v1/territorios", ("X-Forwarded-For", "203.0.113.7"))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await cliente.PedirAsync("/v1/territorios", ("X-Forwarded-For", "198.51.100.9"))).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Theory]
    [InlineData("203.0.113.7", "203.0.113.7")]
    [InlineData("::ffff:203.0.113.7", "203.0.113.7")]
    [InlineData("2001:db8:1:2:aaaa:bbbb:cccc:dddd", "20010db800010002::/64")]
    [InlineData("2001:db8:1:2:1111:2222:3333:4444", "20010db800010002::/64")]
    public void Las_ipv6_se_agrupan_por_red_y_las_ipv4_mapeadas_cuentan_como_ipv4(string direccion, string esperada)
    {
        Seguridad.LimitadorPorIp.Clave(IPAddress.Parse(direccion)).ShouldBe(esperada);
    }
}

public class CabecerasYCorsTests(ApiCompartida compartida) : IClassFixture<ApiCompartida>
{
    private readonly HttpClient _cliente = compartida.Cliente;

    [Theory]
    [InlineData("/v1/territorios")]
    [InlineData("/")]
    [InlineData("/health/live")]
    public async Task Todas_las_respuestas_llevan_las_cabeceras_de_seguridad(string ruta)
    {
        using var respuesta = await _cliente.GetAsync(ruta, TestContext.Current.CancellationToken);

        respuesta.Una("X-Content-Type-Options").ShouldBe("nosniff");
        respuesta.Una("Referrer-Policy").ShouldBe("no-referrer");
        respuesta.Una("Content-Security-Policy").ShouldContain("default-src 'self'");
        respuesta.Una("Content-Security-Policy").ShouldContain("frame-ancestors 'none'");
        respuesta.Una("Content-Security-Policy").ShouldNotContain("unsafe-inline");
        respuesta.Una("X-Frame-Options").ShouldBe("DENY");
    }

    [Fact]
    public async Task La_politica_de_contenido_permite_el_importmap_de_blazor_por_su_hash_y_no_por_unsafe_inline()
    {
        using var respuesta = await _cliente.GetAsync("/", TestContext.Current.CancellationToken);
        var pagina = await respuesta.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // El navegador calcula el hash sobre el script con los saltos de línea normalizados a LF.
        var importmap = System.Text.RegularExpressions.Regex.Match(pagina, "<script type=\"importmap\">(?<c>.*?)</script>", System.Text.RegularExpressions.RegexOptions.Singleline).Groups["c"].Value;
        var hash = Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(importmap.ReplaceLineEndings("\n"))));

        importmap.ShouldNotBeNullOrWhiteSpace();
        respuesta.Una("Content-Security-Policy").ShouldContain($"'sha256-{hash}'");
        respuesta.Una("Content-Security-Policy").ShouldNotContain("unsafe-inline");
        pagina.ShouldNotContain("<script>"); // ningún otro script en línea
    }

    [Fact]
    public async Task La_documentacion_de_la_api_queda_fuera_de_la_politica_estricta_porque_usa_scripts_en_linea()
    {
        using var respuesta = await _cliente.GetAsync("/scalar/v1", TestContext.Current.CancellationToken);

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);
        respuesta.Una("Content-Security-Policy").ShouldBeEmpty();
        respuesta.Una("X-Content-Type-Options").ShouldBe("nosniff");
    }

    [Fact]
    public async Task Cualquier_origen_puede_leer_la_api_desde_el_navegador()
    {
        using var respuesta = await _cliente.PedirAsync("/v1/territorios", ("Origin", "https://otra-web.example"));

        respuesta.Una("Access-Control-Allow-Origin").ShouldBe("*");
        respuesta.Una("Access-Control-Expose-Headers").ShouldContain("ETag");
        respuesta.Una("Access-Control-Expose-Headers").ShouldContain("RateLimit-Remaining");
    }

    [Fact]
    public async Task El_preflight_solo_permite_metodos_de_lectura()
    {
        using var lectura = new HttpRequestMessage(HttpMethod.Options, "/v1/demanda");
        lectura.Headers.Add("Origin", "https://otra-web.example");
        lectura.Headers.Add("Access-Control-Request-Method", "GET");
        using var escritura = new HttpRequestMessage(HttpMethod.Options, "/v1/demanda");
        escritura.Headers.Add("Origin", "https://otra-web.example");
        escritura.Headers.Add("Access-Control-Request-Method", "DELETE");

        using var ok = await _cliente.SendAsync(lectura, TestContext.Current.CancellationToken);
        using var denegado = await _cliente.SendAsync(escritura, TestContext.Current.CancellationToken);

        ok.Una("Access-Control-Allow-Methods").ShouldContain("GET");
        ok.Una("Access-Control-Allow-Methods").ShouldNotContain("DELETE");
        denegado.Una("Access-Control-Allow-Methods").ShouldNotContain("DELETE");
    }

    [Fact]
    public async Task Las_respuestas_se_comprimen_cuando_el_cliente_lo_admite()
    {
        using var respuesta = await _cliente.PedirAsync("/v1/demanda?territorio=region-murcia", ("Accept-Encoding", "br, gzip"));

        respuesta.Content.Headers.ContentEncoding.ShouldContain("br");
    }

    [Fact]
    public async Task Los_errores_internos_no_filtran_detalles()
    {
        // Una ruta que no existe en el explorador cae en la página; una API que falla no debe enseñar la pila.
        using var respuesta = await _cliente.GetAsync("/v1/demanda?territorio=%00", TestContext.Current.CancellationToken);

        respuesta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await respuesta.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldNotContain("at MurciaDatos");
    }
}

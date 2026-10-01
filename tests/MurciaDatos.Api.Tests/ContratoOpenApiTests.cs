using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;

using Shouldly;

namespace MurciaDatos.Api.Tests;

/// <summary>
/// El contrato de la API (OpenAPI) está guardado en el repositorio: cualquier cambio en las rutas, los parámetros o
/// las respuestas hace fallar este test hasta que alguien lo revise y lo acepte. Así un cambio que rompería a los
/// clientes no llega por accidente, y una revisión de código ve exactamente qué cambia del contrato.
/// </summary>
/// <remarks>
/// Para aceptar un cambio consciente: <c>ACTUALIZAR_CONTRATO=1 dotnet run --project tests/MurciaDatos.Api.Tests</c>
/// y revisar el diff de <c>Contrato/openapi.v1.json</c>.
/// </remarks>
public class ContratoOpenApiTests(ApiCompartida compartida) : IClassFixture<ApiCompartida>
{
    private static readonly JsonSerializerOptions Formato = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly HttpClient _cliente = compartida.Cliente;

    private static string RutaSnapshot([CallerFilePath] string ficheroActual = "") =>
        Path.Combine(Path.GetDirectoryName(ficheroActual)!, "Contrato", "openapi.v1.json");

    private async Task<JsonObject> ObtenerContratoAsync()
    {
        using var respuesta = await _cliente.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);

        var documento = JsonNode.Parse(await respuesta.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!.AsObject();

        // La dirección del servidor depende de dónde se ejecute el test: no forma parte del contrato.
        documento.Remove("servers");
        return documento;
    }

    [Fact]
    public async Task El_contrato_publicado_es_el_que_esta_guardado_en_el_repositorio()
    {
        var actual = (await ObtenerContratoAsync()).ToJsonString(Formato).ReplaceLineEndings("\n") + "\n";
        var ruta = RutaSnapshot();

        if (Environment.GetEnvironmentVariable("ACTUALIZAR_CONTRATO") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
            await File.WriteAllTextAsync(ruta, actual, TestContext.Current.CancellationToken);
            return;
        }

        File.Exists(ruta).ShouldBeTrue("falta Contrato/openapi.v1.json: genéralo con ACTUALIZAR_CONTRATO=1");
        var guardado = (await File.ReadAllTextAsync(ruta, TestContext.Current.CancellationToken)).ReplaceLineEndings("\n");

        actual.ShouldBe(guardado, "el contrato de la API ha cambiado; si es intencionado, actualiza el fichero con ACTUALIZAR_CONTRATO=1 y revisa el diff");
    }

    [Fact]
    public async Task Todas_las_operaciones_tienen_identificador_unico_resumen_y_etiqueta()
    {
        var contrato = await ObtenerContratoAsync();
        var ids = new List<string>();

        foreach (var (ruta, item) in contrato["paths"]!.AsObject())
        {
            foreach (var (metodo, operacion) in item!.AsObject())
            {
                var etiqueta = $"{metodo.ToUpperInvariant()} {ruta}";
                metodo.ShouldBe("get", etiqueta); // solo lectura
                operacion!["operationId"]?.GetValue<string>().ShouldNotBeNullOrWhiteSpace(etiqueta);
                operacion["summary"]?.GetValue<string>().ShouldNotBeNullOrWhiteSpace(etiqueta);
                operacion["tags"]!.AsArray().Count.ShouldBeGreaterThan(0, etiqueta);
                ids.Add(operacion["operationId"]!.GetValue<string>());
            }
        }

        ids.Count.ShouldBe(10);
        ids.ShouldBeUnique();
    }

    [Fact]
    public async Task Los_parametros_de_las_series_estan_documentados_con_su_descripcion()
    {
        var contrato = await ObtenerContratoAsync();

        var parametros = contrato["paths"]!["/v1/demanda"]!["get"]!["parameters"]!.AsArray();

        parametros.Select(p => p!["name"]!.GetValue<string>()).ShouldBe(["territorio", "tipo", "residencia", "desde", "hasta", "agregacion", "medidas", "formato", "excel"], ignoreOrder: true);
        parametros.ShouldAllBe(p => !string.IsNullOrWhiteSpace(p!["description"]!.GetValue<string>()));
    }

    [Fact]
    public async Task Las_series_documentan_json_csv_304_y_los_errores_estandar()
    {
        var contrato = await ObtenerContratoAsync();

        var respuestas = contrato["paths"]!["/v1/demanda"]!["get"]!["responses"]!.AsObject();

        respuestas.Select(r => r.Key).ShouldBe(["200", "304", "400", "429", "503"], ignoreOrder: true);
        respuestas["200"]!["content"]!.AsObject().Select(c => c.Key).ShouldContain("application/json");
        respuestas["200"]!["content"]!.AsObject().Select(c => c.Key).ShouldContain("text/csv");
    }
}

using System.Text.Json;

using MurciaDatos.Explorador.Servicios;

namespace MurciaDatos.Explorador.Logica;

/// <summary>Convierte la respuesta de la API en las series de la gráfica, con nombres legibles.</summary>
public static class ConstructorDeSeries
{
    /// <param name="agregacion">mes, trimestre o anio: decide cuántos meses debe tener un periodo para estar completo.</param>
    public static IReadOnlyList<SerieGrafica> Construir(RespuestaSerieApi respuesta, string medida, Catalogos catalogos, string agregacion = "mes")
    {
        var esperados = agregacion switch { "anio" => 12, "trimestre" => 3, _ => 1 };
        var grupos = respuesta.Datos
            .GroupBy(f => (Territorio: Texto(f, "territorio"), Tipo: Texto(f, "tipo"), Residencia: f.ContainsKey("residencia") ? Texto(f, "residencia") : null))
            .ToList();

        // Solo se nombra lo que distingue a unas series de otras: si todas son de un territorio, no se repite en cada leyenda.
        var variaTerritorio = grupos.Select(g => g.Key.Territorio).Distinct().Count() > 1;
        var variaTipo = grupos.Select(g => g.Key.Tipo).Distinct().Count() > 1;
        var variaResidencia = grupos.Select(g => g.Key.Residencia).Distinct().Count() > 1;

        var series = new List<SerieGrafica>();
        foreach (var grupo in grupos)
        {
            var partes = new List<string>();
            if (variaTerritorio)
            {
                partes.Add(catalogos.Territorios.FirstOrDefault(t => t.Id == grupo.Key.Territorio)?.Nombre ?? grupo.Key.Territorio);
            }

            if (variaTipo)
            {
                partes.Add(catalogos.Tipos.FirstOrDefault(t => t.Id == grupo.Key.Tipo)?.Nombre ?? grupo.Key.Tipo);
            }

            if (variaResidencia && grupo.Key.Residencia is { } residencia)
            {
                partes.Add(catalogos.Residencias.FirstOrDefault(r => r.Id == residencia)?.Nombre ?? residencia);
            }

            if (partes.Count == 0)
            {
                partes.Add(catalogos.Territorios.FirstOrDefault(t => t.Id == grupo.Key.Territorio)?.Nombre ?? grupo.Key.Territorio);
            }

            series.Add(new SerieGrafica(
                string.Join(" · ", partes),
                [.. grupo.Select(f =>
                {
                    int? meses = f.TryGetValue("meses", out var m) && m.ValueKind == JsonValueKind.Number ? m.GetInt32() : null;
                    return new PuntoDeGrafica(Texto(f, "periodo"), Numero(f, medida), meses is { } n && n < esperados, meses);
                })]));
        }

        return series;
    }

    public static string Texto(Dictionary<string, JsonElement> fila, string clave) =>
        fila.TryGetValue(clave, out var valor) && valor.ValueKind == JsonValueKind.String ? valor.GetString() ?? string.Empty : string.Empty;

    /// <summary>El valor numérico de una medida, o null si el dato no existe (la API nunca usa 0 para eso).</summary>
    public static double? Numero(Dictionary<string, JsonElement> fila, string clave) =>
        fila.TryGetValue(clave, out var valor) && valor.ValueKind == JsonValueKind.Number ? valor.GetDouble() : null;
}

/// <summary>Las llamadas equivalentes a lo que se está viendo, para copiarlas y usarlas fuera del explorador.</summary>
public static class ConstructorDeLlamadas
{
    public static string Curl(string urlBase, EstadoDeConsulta estado) =>
        $"curl -s \"{Direccion(urlBase, estado)}\"";

    public static string CurlCsv(string urlBase, EstadoDeConsulta estado) =>
        $"curl -s -o {estado.Recurso}.csv \"{Direccion(urlBase, estado, formato: "csv")}\"";

    public static string Python(string urlBase, EstadoDeConsulta estado) =>
        $"import pandas as pd\n\ndf = pd.read_csv(\"{Direccion(urlBase, estado, formato: "csv")}\")";

    public static string Direccion(string urlBase, EstadoDeConsulta estado, string? formato = null, bool excel = false) =>
        $"{urlBase.TrimEnd('/')}/v1/{estado.Recurso}?{estado.ConsultaApi(formato, excel)}";
}

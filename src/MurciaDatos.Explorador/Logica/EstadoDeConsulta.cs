using System.Globalization;
using System.Web;

namespace MurciaDatos.Explorador.Logica;

/// <summary>
/// Lo que el usuario ha elegido en el explorador. Es lo único que vive en la URL de la página: copiarla es
/// compartir la consulta. Se traduce a la llamada de la API (<see cref="ConsultaApi"/>) y a la URL de la página
/// (<see cref="ParaLaPagina"/>), y se puede reconstruir desde ella (<see cref="DesdeLaPagina"/>).
/// </summary>
public sealed record EstadoDeConsulta
{
    public static readonly IReadOnlyList<string> Recursos = ["demanda", "oferta", "precios"];

    public string Recurso { get; init; } = "demanda";

    public IReadOnlyList<string> Territorios { get; init; } = ["region-murcia"];

    public IReadOnlyList<string> Tipos { get; init; } = ["hotel"];

    /// <summary>Solo en la demanda: <c>total</c>, <c>espana</c>, <c>extranjero</c> o <c>separadas</c> (una serie por residencia).</summary>
    public string Residencia { get; init; } = "total";

    public string Medida { get; init; } = "pernoctaciones";

    public string Agregacion { get; init; } = "mes";

    public string? Desde { get; init; } = "2019-01";

    public string? Hasta { get; init; }

    public static EstadoDeConsulta Inicial(string recurso) => recurso switch
    {
        "oferta" => new EstadoDeConsulta { Recurso = "oferta", Medida = "ocupacion_plazas", Residencia = "total" },
        "precios" => new EstadoDeConsulta { Recurso = "precios", Medida = "indice_precios", Residencia = "total" },
        _ => new EstadoDeConsulta(),
    };

    /// <summary>Los parámetros de la llamada a la API (sin el «?»), con los mismos nombres que usa cualquier otro cliente.</summary>
    public string ConsultaApi(string? formato = null, bool excel = false)
    {
        var parametros = new List<(string Nombre, string Valor)>();
        if (Territorios.Count > 0)
        {
            parametros.Add(("territorio", string.Join(",", Territorios)));
        }

        if (Tipos.Count > 0)
        {
            parametros.Add(("tipo", string.Join(",", Tipos)));
        }

        if (Recurso == "demanda")
        {
            parametros.Add(("residencia", Residencia == "separadas" ? "espana,extranjero" : Residencia));
        }

        if (!string.IsNullOrEmpty(Desde))
        {
            parametros.Add(("desde", Desde));
        }

        if (!string.IsNullOrEmpty(Hasta))
        {
            parametros.Add(("hasta", Hasta));
        }

        parametros.Add(("agregacion", Agregacion));
        parametros.Add(("medidas", Medida));

        if (formato is not null)
        {
            parametros.Add(("formato", formato));
            if (excel)
            {
                parametros.Add(("excel", "true"));
            }
        }

        // Los valores son identificadores en kebab-case y fechas: no necesitan codificarse, y así la URL se lee.
        return string.Join("&", parametros.Select(p => $"{p.Nombre}={p.Valor}"));
    }

    /// <summary>La cadena de consulta de la URL de la página (con «?»).</summary>
    public string ParaLaPagina()
    {
        var partes = new List<string> { $"recurso={Recurso}" };
        if (Territorios.Count > 0)
        {
            partes.Add($"territorio={Codificar(string.Join(",", Territorios))}");
        }

        if (Tipos.Count > 0)
        {
            partes.Add($"tipo={Codificar(string.Join(",", Tipos))}");
        }

        if (Recurso == "demanda")
        {
            partes.Add($"residencia={Residencia}");
        }

        partes.Add($"medida={Medida}");
        partes.Add($"agregacion={Agregacion}");
        // «desde=» vacío significa «desde el primer mes»; si se omitiera, al volver a leer la URL saldría el valor por defecto.
        partes.Add($"desde={Desde}");
        if (!string.IsNullOrEmpty(Hasta))
        {
            partes.Add($"hasta={Hasta}");
        }

        return "?" + string.Join("&", partes);
    }

    /// <summary>Reconstruye el estado desde la cadena de consulta de la página; lo que falte o no sea válido toma el valor por defecto.</summary>
    public static EstadoDeConsulta DesdeLaPagina(string consulta)
    {
        var valores = HttpUtility.ParseQueryString(consulta.TrimStart('?'));
        var recurso = valores["recurso"] is { } r && Recursos.Contains(r) ? r : "demanda";
        var inicial = Inicial(recurso);

        static IReadOnlyList<string>? Lista(string? texto) =>
            texto is null ? null : [.. texto.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.Ordinal)];

        return inicial with
        {
            Territorios = Lista(valores["territorio"]) ?? inicial.Territorios,
            Tipos = Lista(valores["tipo"]) ?? inicial.Tipos,
            Residencia = valores["residencia"] is "total" or "espana" or "extranjero" or "separadas" ? valores["residencia"]! : inicial.Residencia,
            Medida = string.IsNullOrWhiteSpace(valores["medida"]) ? inicial.Medida : valores["medida"]!,
            Agregacion = valores["agregacion"] is "mes" or "trimestre" or "anio" ? valores["agregacion"]! : inicial.Agregacion,
            // Sin el parámetro: el valor por defecto. Vacío («desde=»): desde el primer mes. Con basura: el valor por defecto.
            Desde = valores["desde"] switch
            {
                null => inicial.Desde,
                "" => null,
                var texto => EsMes(texto) ? texto : inicial.Desde,
            },
            Hasta = EsMes(valores["hasta"]) ? valores["hasta"] : null,
        };
    }

    public static bool EsMes(string? texto) =>
        texto is { Length: 7 } && texto[4] == '-'
        && int.TryParse(texto.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var anio) && anio is >= 1900 and <= 2200
        && int.TryParse(texto.AsSpan(5, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var mes) && mes is >= 1 and <= 12;

    private static string Codificar(string texto) => Uri.EscapeDataString(texto).Replace("%2C", ",", StringComparison.Ordinal);

    /// <summary>Igualdad por valor también para las listas (el record compara listas por referencia).</summary>
    public bool EquivaleA(EstadoDeConsulta otro) => ParaLaPagina() == otro.ParaLaPagina();
}

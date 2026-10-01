using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;

using MurciaDatos.Datos.Consultas;

namespace MurciaDatos.Api.Formatos;

/// <summary>Una medida disponible en un recurso, tal y como se describe en <c>meta.medidas</c>.</summary>
public sealed record MedidaPublica(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("unidad")] string Unidad,
    [property: JsonPropertyName("agregado")] string Agregado,
    [property: JsonPropertyName("descripcion")] string Descripcion);

public sealed record MetaDeSerie(
    [property: JsonPropertyName("version_datos")] string VersionDatos,
    [property: JsonPropertyName("fuentes")] IReadOnlyList<string> Fuentes,
    [property: JsonPropertyName("provisional_desde")] string? ProvisionalDesde,
    [property: JsonPropertyName("agregacion")] string Agregacion,
    [property: JsonPropertyName("medidas")] IReadOnlyList<MedidaPublica> Medidas,
    [property: JsonPropertyName("total_filas")] int TotalFilas);

/// <summary>
/// Respuesta de las series. Cada fila de <c>datos</c> tiene las claves <c>periodo</c>, <c>territorio</c>, <c>tipo</c>,
/// (<c>residencia</c> en la demanda), <c>fuente</c>, <c>meses</c> (si se agrega), <c>provisional</c> y una clave por medida pedida.
/// Un dato que no existe es <c>null</c>, nunca 0.
/// </summary>
public sealed record RespuestaDeSerie(
    [property: JsonPropertyName("datos")] IReadOnlyList<IReadOnlyDictionary<string, object?>> Datos,
    [property: JsonPropertyName("meta")] MetaDeSerie Meta);

public static class ConstructorDeRespuestas
{
    /// <summary>Los nombres de las columnas de una consulta, en el orden en que se publican (JSON y CSV).</summary>
    public static IReadOnlyList<string> Columnas(ConsultaDeSerie consulta)
    {
        var columnas = new List<string> { "periodo", "territorio", "tipo" };
        if (consulta.Hecho == Hecho.Demanda)
        {
            columnas.Add("residencia");
        }

        columnas.Add("fuente");
        if (consulta.Agregacion != Agregacion.Mes)
        {
            columnas.Add("meses");
        }

        columnas.Add("provisional");
        columnas.AddRange(consulta.Medidas.Select(m => m.Id));
        return columnas;
    }

    public static IReadOnlyList<IReadOnlyDictionary<string, object?>> Filas(ConsultaDeSerie consulta, IReadOnlyList<FilaDeSerie> filas)
    {
        var resultado = new List<IReadOnlyDictionary<string, object?>>(filas.Count);
        foreach (var fila in filas)
        {
            var objeto = new Dictionary<string, object?>
            {
                ["periodo"] = fila.Periodo,
                ["territorio"] = fila.Territorio,
                ["tipo"] = fila.Tipo,
            };

            if (consulta.Hecho == Hecho.Demanda)
            {
                objeto["residencia"] = fila.Residencia;
            }

            objeto["fuente"] = fila.Fuente;
            if (consulta.Agregacion != Agregacion.Mes)
            {
                objeto["meses"] = fila.Meses;
            }

            objeto["provisional"] = fila.Provisional;
            for (var i = 0; i < consulta.Medidas.Count; i++)
            {
                objeto[consulta.Medidas[i].Id] = fila.Valores[i];
            }

            resultado.Add(objeto);
        }

        return resultado;
    }

    public static MetaDeSerie Meta(InstantaneaDatos instantanea, ConsultaDeSerie consulta, int totalFilas) =>
        new(
            instantanea.Etiqueta,
            instantanea.Contrato.Fuentes,
            MesComoTexto(instantanea.Catalogo.Disponibilidad[consulta.Hecho].ProvisionalDesde),
            consulta.Agregacion switch
            {
                Agregacion.Mes => "mes",
                Agregacion.Trimestre => "trimestre",
                _ => "anio",
            },
            [.. consulta.Medidas.Select(m => new MedidaPublica(m.Id, m.Unidad, m.Agregado == TipoDeAgregado.Suma ? "suma" : "media", m.Descripcion))],
            totalFilas);

    public static string? MesComoTexto(int? fechaId) =>
        fechaId is { } f ? string.Create(CultureInfo.InvariantCulture, $"{f / 100:0000}-{f % 100:00}") : null;
}

/// <summary>Escribe una tabla como CSV (RFC 4180), con la variante cómoda para Excel en español.</summary>
public static class EscritorCsv
{
    public static string Escribir(IReadOnlyList<string> columnas, IReadOnlyList<IReadOnlyDictionary<string, object?>> filas, bool excel)
    {
        var separador = excel ? ';' : ',';
        var cultura = excel ? CultureInfo.GetCultureInfo("es-ES") : CultureInfo.InvariantCulture;
        var texto = new StringBuilder();

        if (excel)
        {
            texto.Append('﻿'); // BOM: Excel reconoce así el UTF-8
        }

        texto.Append(string.Join(separador, columnas)).Append("\r\n");
        foreach (var fila in filas)
        {
            for (var i = 0; i < columnas.Count; i++)
            {
                if (i > 0)
                {
                    texto.Append(separador);
                }

                texto.Append(Celda(fila[columnas[i]], separador, cultura));
            }

            texto.Append("\r\n");
        }

        return texto.ToString();
    }

    internal static string Celda(object? valor, char separador, CultureInfo cultura) => valor switch
    {
        null => string.Empty,
        bool b => b ? "true" : "false",
        string s => Texto(s, separador),
        IFormattable f => f.ToString(null, cultura),
        _ => Texto(valor.ToString() ?? string.Empty, separador),
    };

    private static string Texto(string texto, char separador)
    {
        // Una hoja de cálculo ejecuta como fórmula lo que empiece por estos caracteres: se neutraliza.
        if (texto.Length > 0 && texto[0] is '=' or '+' or '@' or '\t' or '\r' || (texto.Length > 1 && texto[0] == '-' && !char.IsAsciiDigit(texto[1])))
        {
            texto = "'" + texto;
        }

        return texto.IndexOfAny([separador, '"', '\r', '\n']) >= 0
            ? "\"" + texto.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : texto;
    }
}

using System.Globalization;
using System.Text;

using DuckDB.NET.Data;

namespace MurciaDatos.Datos.Consultas;

/// <summary>
/// Una consulta de series ya validada. Las listas vacías significan «sin filtrar». Las medidas vienen del
/// catálogo cerrado (<see cref="CatalogoDeMedidas"/>): nada de lo que llegue por la URL acaba escrito en el SQL.
/// </summary>
/// <param name="TotalResidencia">Suma los residentes en España y en el extranjero (demanda) en una sola fila de residencia «total».
/// Un mes al que le falte alguna de las residencias no tiene total (null): sumar solo una parte lo falsearía.</param>
/// <param name="NumeroDeResidencias">Cuántas residencias hay que sumar en cada mes para que el total sea válido.</param>
/// <param name="Desde">Primer mes, como AAAAMM.</param>
/// <param name="Hasta">Último mes, como AAAAMM.</param>
public sealed record ConsultaDeSerie(
    Hecho Hecho,
    IReadOnlyList<string> Territorios,
    IReadOnlyList<string> Tipos,
    IReadOnlyList<string> Residencias,
    bool TotalResidencia,
    int? Desde,
    int? Hasta,
    Agregacion Agregacion,
    IReadOnlyList<Medida> Medidas,
    int NumeroDeResidencias = 0);

/// <param name="Meses">Meses con dato que se han agrupado en el periodo (un año incompleto tiene menos de 12).</param>
/// <param name="Provisional">Algún mes del periodo es provisional.</param>
/// <param name="Valores">Un valor por medida, en el orden pedido; null si no hay dato (nunca 0 por falta de dato).</param>
public sealed record FilaDeSerie(
    string Periodo,
    string Territorio,
    string Tipo,
    string? Residencia,
    string Fuente,
    int Meses,
    bool Provisional,
    IReadOnlyList<object?> Valores);

public sealed record ResultadoDeSerie(IReadOnlyList<FilaDeSerie> Filas, bool Truncado);

/// <summary>Ejecuta las consultas de series contra una instantánea de DuckDB.</summary>
public static class ConsultasDeSeries
{
    private const int ColumnasFijas = 7; // periodo, territorio, tipo, residencia, fuente, meses, provisional

    public static ResultadoDeSerie Ejecutar(InstantaneaDatos.Prestamo prestamo, ConsultaDeSerie consulta, int maxFilas, CancellationToken cancelacion)
    {
        cancelacion.ThrowIfCancellationRequested();
        var (sql, parametros) = Construir(consulta, maxFilas);

        using var conexion = prestamo.AbrirConexion();
        using var comando = conexion.CreateCommand();
        comando.CommandText = sql;
        foreach (var valor in parametros)
        {
            comando.Parameters.Add(new DuckDBParameter(valor));
        }

        // Una consulta que se pasa de tiempo (o cuyo cliente se ha ido) se interrumpe en DuckDB.
        using var registro = cancelacion.Register(comando.Cancel);

        var filas = new List<FilaDeSerie>();
        try
        {
            using var lector = comando.ExecuteReader();
            while (lector.Read())
            {
                var valores = new object?[consulta.Medidas.Count];
                for (var i = 0; i < valores.Length; i++)
                {
                    var celda = lector.GetValue(ColumnasFijas + i);
                    valores[i] = celda is DBNull ? null : celda;
                }

                filas.Add(new FilaDeSerie(
                    lector.GetString(0),
                    lector.GetString(1),
                    lector.GetString(2),
                    lector.IsDBNull(3) ? null : lector.GetString(3),
                    lector.GetString(4),
                    Convert.ToInt32(lector.GetValue(5), CultureInfo.InvariantCulture),
                    lector.GetBoolean(6),
                    valores));
            }
        }
        catch (DuckDBException) when (cancelacion.IsCancellationRequested)
        {
            cancelacion.ThrowIfCancellationRequested();
            throw;
        }

        return filas.Count > maxFilas
            ? new ResultadoDeSerie(filas.Take(maxFilas).ToList(), Truncado: true)
            : new ResultadoDeSerie(filas, Truncado: false);
    }

    /// <summary>
    /// El SQL de una consulta: sus únicas partes variables son nombres del catálogo cerrado y números; los valores
    /// de los filtros viajan siempre como parámetros.
    /// </summary>
    internal static (string Sql, List<object> Parametros) Construir(ConsultaDeSerie consulta, int maxFilas)
    {
        var parametros = new List<object>();
        var sql = new StringBuilder();

        var periodo = consulta.Agregacion switch
        {
            Agregacion.Mes => "printf('%04d-%02d', d.anio, d.mes)",
            Agregacion.Trimestre => "printf('%04d-T%d', d.anio, d.trimestre)",
            Agregacion.Anio => "CAST(d.anio AS VARCHAR)",
            _ => throw new ArgumentOutOfRangeException(nameof(consulta)),
        };

        var agrupaPorResidencia = consulta.Hecho == Hecho.Demanda && !consulta.TotalResidencia;
        var residencia = consulta.Hecho switch
        {
            Hecho.Demanda when consulta.TotalResidencia => "'total'",
            Hecho.Demanda => "f.residencia_id",
            _ => "CAST(NULL AS VARCHAR)",
        };

        var meses = string.Join(" OR ", consulta.Medidas.Select(m => $"f.{m.Columna} IS NOT NULL"));
        sql.Append("SELECT ").Append(periodo).Append(" AS periodo, f.territorio_id, f.tipo_alojamiento_id, ").Append(residencia)
            .Append(" AS residencia, MIN(f.fuente) AS fuente, CAST(COUNT(DISTINCT CASE WHEN ").Append(meses)
            .Append(" THEN f.fecha_id END) AS INTEGER) AS meses, BOOL_OR(f.es_provisional) AS provisional");

        foreach (var medida in consulta.Medidas)
        {
            sql.Append(", ").Append(medida.Agregado == TipoDeAgregado.Suma
                ? $"CAST(SUM(f.{medida.Columna}) AS BIGINT)"
                : $"ROUND(AVG(f.{medida.Columna}), 2)");
        }

        sql.Append(" FROM ");
        if (consulta.Hecho == Hecho.Demanda && consulta.TotalResidencia)
        {
            // Primero el total de cada mes, y solo si tiene todas las residencias; después se agrupa por periodo.
            sql.Append("(SELECT f.fecha_id, f.territorio_id, f.tipo_alojamiento_id, MIN(f.fuente) AS fuente, BOOL_OR(f.es_provisional) AS es_provisional");
            foreach (var medida in consulta.Medidas)
            {
                sql.Append(", CASE WHEN COUNT(f.").Append(medida.Columna).Append(") = ").Append(consulta.NumeroDeResidencias.ToString(CultureInfo.InvariantCulture))
                    .Append(" THEN SUM(f.").Append(medida.Columna).Append(") END AS ").Append(medida.Columna);
            }

            sql.Append(" FROM ").Append(CatalogoDeMedidas.Tabla(consulta.Hecho)).Append(" f WHERE 1 = 1");
            AnadirFiltros(sql, parametros, consulta);
            sql.Append(" GROUP BY 1, 2, 3) f JOIN gold.dim_fecha d ON d.fecha_id = f.fecha_id");
        }
        else
        {
            sql.Append(CatalogoDeMedidas.Tabla(consulta.Hecho)).Append(" f JOIN gold.dim_fecha d ON d.fecha_id = f.fecha_id WHERE 1 = 1");
            AnadirFiltros(sql, parametros, consulta);
        }

        var agrupacion = agrupaPorResidencia ? "1, 2, 3, 4" : "1, 2, 3";
        sql.Append(" GROUP BY ").Append(agrupacion).Append(" ORDER BY ").Append(agrupacion)
            .Append(" LIMIT ").Append((maxFilas + 1).ToString(CultureInfo.InvariantCulture));

        return (sql.ToString(), parametros);
    }

    private static void AnadirFiltros(StringBuilder sql, List<object> parametros, ConsultaDeSerie consulta)
    {
        AnadirLista(sql, parametros, "f.territorio_id", consulta.Territorios);
        AnadirLista(sql, parametros, "f.tipo_alojamiento_id", consulta.Tipos);
        if (consulta.Hecho == Hecho.Demanda)
        {
            AnadirLista(sql, parametros, "f.residencia_id", consulta.Residencias);
        }

        if (consulta.Desde is { } desde)
        {
            sql.Append(" AND f.fecha_id >= ?");
            parametros.Add(desde);
        }

        if (consulta.Hasta is { } hasta)
        {
            sql.Append(" AND f.fecha_id <= ?");
            parametros.Add(hasta);
        }
    }

    private static void AnadirLista(StringBuilder sql, List<object> parametros, string columna, IReadOnlyList<string> valores)
    {
        if (valores.Count == 0)
        {
            return;
        }

        sql.Append(" AND ").Append(columna).Append(" IN (").Append(string.Join(", ", valores.Select(_ => "?"))).Append(')');
        parametros.AddRange(valores);
    }
}

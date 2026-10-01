using System.Globalization;

using DuckDB.NET.Data;

using MurciaDatos.Datos.Contrato;

namespace MurciaDatos.Datos.Consultas;

/// <summary>
/// Abre un fichero <c>.duckdb</c> y lo comprueba antes de aceptarlo: que su esquema sea el esperado, que las
/// filas coincidan con las del contrato, que las claves no se repitan y que no haya hechos huérfanos. Si algo
/// falla, la versión se rechaza y se sigue sirviendo la anterior.
/// </summary>
public static class CargadorDeInstantanea
{
    private static readonly (Hecho Hecho, string Tabla, string Claves)[] Hechos =
    [
        (Hecho.Demanda, "fct_demanda_mensual", "fecha_id, territorio_id, tipo_alojamiento_id, residencia_id"),
        (Hecho.Oferta, "fct_oferta_mensual", "fecha_id, territorio_id, tipo_alojamiento_id"),
        (Hecho.Precios, "fct_precios_mensual", "fecha_id, territorio_id, tipo_alojamiento_id"),
    ];

    /// <exception cref="ContratoInvalidoException">El fichero no cumple el contrato o los controles de calidad.</exception>
    public static InstantaneaDatos Abrir(
        string rutaFichero,
        string? directorioABorrar,
        string etiqueta,
        string huella,
        ContratoDatos contrato,
        bool sumasVerificadas,
        TimeProvider reloj)
    {
        var controles = new List<ControlDeCalidad>
        {
            new("Contrato compatible", true, $"Contrato {contrato.VersionContrato} (versión mayor {ContratoEsperado.VersionMayor} soportada)."),
            new("Sumas SHA-256", sumasVerificadas, sumasVerificadas
                ? "El fichero coincide con la suma publicada en la release."
                : "No se ha verificado la suma del fichero."),
        };

        var problemas = ValidadorDeContrato.Validar(contrato).ToList();
        if (problemas.Count > 0)
        {
            throw new ContratoInvalidoException(string.Join(" ", problemas));
        }

        DuckDBConnection conexion;
        try
        {
            conexion = InstantaneaDatos.Conectar(rutaFichero);
        }
        catch (DuckDBException ex)
        {
            throw new ContratoInvalidoException($"No se puede abrir el fichero de datos: {ex.Message}");
        }

        using (conexion)
        {
            controles.Add(ComprobarEsquema(conexion));
            controles.Add(ComprobarFilas(conexion, contrato));
            controles.Add(ComprobarClaves(conexion));
            controles.Add(ComprobarIntegridad(conexion));

            var fallos = controles.Where(c => !c.Correcto).ToList();
            if (fallos.Count > 0)
            {
                throw new ContratoInvalidoException(string.Join(" ", fallos.Select(f => $"{f.Nombre}: {f.Detalle}")));
            }

            var catalogo = LeerCatalogo(conexion);
            return new InstantaneaDatos(etiqueta, huella, rutaFichero, directorioABorrar, contrato, catalogo, controles, reloj.GetUtcNow());
        }
    }

    private static ControlDeCalidad ComprobarEsquema(DuckDBConnection conexion)
    {
        var existentes = new Dictionary<(string Tabla, string Columna), string>();
        using (var lector = Ejecutar(conexion, $"SELECT table_name, column_name, data_type FROM information_schema.columns WHERE table_schema = '{ContratoEsperado.Esquema}'"))
        {
            while (lector.Read())
            {
                existentes[(lector.GetString(0), lector.GetString(1))] = lector.GetString(2);
            }
        }

        var problemas = new List<string>();
        foreach (var (tabla, columnas) in ContratoEsperado.Tablas)
        {
            foreach (var (columna, tipo) in columnas)
            {
                if (!existentes.TryGetValue((tabla, columna), out var real))
                {
                    problemas.Add($"falta {tabla}.{columna}");
                }
                else if (!string.Equals(real, tipo, StringComparison.OrdinalIgnoreCase))
                {
                    problemas.Add($"{tabla}.{columna} es {real} y se esperaba {tipo}");
                }
            }
        }

        return problemas.Count == 0
            ? new ControlDeCalidad("Esquema del fichero", true, "Las tablas y columnas coinciden con las esperadas.")
            : new ControlDeCalidad("Esquema del fichero", false, string.Join("; ", problemas) + ".");
    }

    private static ControlDeCalidad ComprobarFilas(DuckDBConnection conexion, ContratoDatos contrato)
    {
        var problemas = new List<string>();
        foreach (var tabla in ContratoEsperado.Tablas.Keys)
        {
            var reales = Escalar(conexion, $"SELECT COUNT(*) FROM {ContratoEsperado.Esquema}.{tabla}");
            if (reales != contrato.Tablas[tabla].Filas)
            {
                problemas.Add($"{tabla} tiene {reales} filas y el contrato dice {contrato.Tablas[tabla].Filas}");
            }
        }

        return problemas.Count == 0
            ? new ControlDeCalidad("Filas del contrato", true, "El número de filas de cada tabla coincide con el contrato.")
            : new ControlDeCalidad("Filas del contrato", false, string.Join("; ", problemas) + ".");
    }

    private static ControlDeCalidad ComprobarClaves(DuckDBConnection conexion)
    {
        var problemas = new List<string>();
        foreach (var (_, tabla, claves) in Hechos)
        {
            var duplicadas = Escalar(conexion, $"SELECT COUNT(*) FROM (SELECT {claves} FROM {ContratoEsperado.Esquema}.{tabla} GROUP BY {claves} HAVING COUNT(*) > 1)");
            if (duplicadas > 0)
            {
                problemas.Add($"{tabla} tiene {duplicadas} claves repetidas");
            }
        }

        return problemas.Count == 0
            ? new ControlDeCalidad("Claves únicas", true, "Ninguna tabla de hechos repite su clave.")
            : new ControlDeCalidad("Claves únicas", false, string.Join("; ", problemas) + ".");
    }

    private static ControlDeCalidad ComprobarIntegridad(DuckDBConnection conexion)
    {
        var problemas = new List<string>();
        foreach (var (_, tabla, _) in Hechos)
        {
            foreach (var (columna, dimension, clave) in new[]
            {
                ("fecha_id", "dim_fecha", "fecha_id"),
                ("territorio_id", "dim_territorio", "territorio_id"),
                ("tipo_alojamiento_id", "dim_tipo_alojamiento", "tipo_alojamiento_id"),
            })
            {
                var huerfanos = Escalar(conexion, $"SELECT COUNT(*) FROM {ContratoEsperado.Esquema}.{tabla} f LEFT JOIN {ContratoEsperado.Esquema}.{dimension} d ON f.{columna} = d.{clave} WHERE d.{clave} IS NULL");
                if (huerfanos > 0)
                {
                    problemas.Add($"{tabla}.{columna} tiene {huerfanos} valores que no existen en {dimension}");
                }
            }
        }

        var sinResidencia = Escalar(conexion, $"SELECT COUNT(*) FROM {ContratoEsperado.Esquema}.fct_demanda_mensual f LEFT JOIN {ContratoEsperado.Esquema}.dim_residencia d ON f.residencia_id = d.residencia_id WHERE d.residencia_id IS NULL");
        if (sinResidencia > 0)
        {
            problemas.Add($"fct_demanda_mensual.residencia_id tiene {sinResidencia} valores que no existen en dim_residencia");
        }

        return problemas.Count == 0
            ? new ControlDeCalidad("Integridad referencial", true, "Todos los hechos apuntan a una dimensión que existe.")
            : new ControlDeCalidad("Integridad referencial", false, string.Join("; ", problemas) + ".");
    }

    private static CatalogoDatos LeerCatalogo(DuckDBConnection conexion)
    {
        var territorios = new List<Territorio>();
        using (var l = Ejecutar(conexion, $"SELECT territorio_id, nombre, nivel, padre_id, fuente, es_desglosado FROM {ContratoEsperado.Esquema}.dim_territorio ORDER BY territorio_id"))
        {
            while (l.Read())
            {
                territorios.Add(new Territorio(l.GetString(0), l.GetString(1), l.GetString(2), l.IsDBNull(3) ? null : l.GetString(3), l.GetString(4), l.GetBoolean(5)));
            }
        }

        var tipos = new List<TipoAlojamiento>();
        using (var l = Ejecutar(conexion, $"SELECT tipo_alojamiento_id, nombre FROM {ContratoEsperado.Esquema}.dim_tipo_alojamiento ORDER BY tipo_alojamiento_id"))
        {
            while (l.Read())
            {
                tipos.Add(new TipoAlojamiento(l.GetString(0), l.GetString(1)));
            }
        }

        var residencias = new List<Residencia>();
        using (var l = Ejecutar(conexion, $"SELECT residencia_id, nombre FROM {ContratoEsperado.Esquema}.dim_residencia ORDER BY residencia_id"))
        {
            while (l.Read())
            {
                residencias.Add(new Residencia(l.GetString(0), l.GetString(1)));
            }
        }

        var disponibilidad = new Dictionary<Hecho, DisponibilidadDeHecho>();
        foreach (var (hecho, tabla, _) in Hechos)
        {
            var nombre = $"{ContratoEsperado.Esquema}.{tabla}";
            var territoriosDelHecho = Textos(conexion, $"SELECT DISTINCT territorio_id FROM {nombre}");
            var tiposDelHecho = Textos(conexion, $"SELECT DISTINCT tipo_alojamiento_id FROM {nombre}");
            var residenciasDelHecho = hecho == Hecho.Demanda ? Textos(conexion, $"SELECT DISTINCT residencia_id FROM {nombre}") : [];

            using var l = Ejecutar(conexion, $"SELECT MIN(fecha_id), MAX(fecha_id), MIN(fecha_id) FILTER (WHERE es_provisional) FROM {nombre}");
            l.Read();
            disponibilidad[hecho] = new DisponibilidadDeHecho(
                territoriosDelHecho,
                tiposDelHecho,
                residenciasDelHecho,
                l.GetInt32(0),
                l.GetInt32(1),
                l.IsDBNull(2) ? null : l.GetInt32(2));
        }

        return new CatalogoDatos(territorios, tipos, residencias, disponibilidad);
    }

    private static HashSet<string> Textos(DuckDBConnection conexion, string sql)
    {
        var resultado = new HashSet<string>(StringComparer.Ordinal);
        using var lector = Ejecutar(conexion, sql);
        while (lector.Read())
        {
            resultado.Add(lector.GetString(0));
        }

        return resultado;
    }

    private static DuckDBDataReader Ejecutar(DuckDBConnection conexion, string sql)
    {
        var comando = conexion.CreateCommand();
        comando.CommandText = sql;
        return comando.ExecuteReader();
    }

    private static long Escalar(DuckDBConnection conexion, string sql)
    {
        using var comando = conexion.CreateCommand();
        comando.CommandText = sql;
        return Convert.ToInt64(comando.ExecuteScalar(), CultureInfo.InvariantCulture);
    }
}

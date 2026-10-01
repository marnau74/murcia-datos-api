using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using DuckDB.NET.Data;

namespace MurciaDatos.Tests.Comunes;

/// <summary>Cómo estropear (o variar) una release de prueba para ejercitar las comprobaciones de la API.</summary>
public sealed record VarianteDeRelease
{
    /// <summary>Multiplica todos los valores: sirve para distinguir una versión de otra.</summary>
    public double Escala { get; init; } = 1;

    public string VersionContrato { get; init; } = "1.0.0";

    /// <summary>Quita esta columna de fct_demanda_mensual (queda registrada en el contrato publicado).</summary>
    public bool SinColumnaViajeros { get; init; }

    /// <summary>Repite una fila de demanda (la clave deja de ser única).</summary>
    public bool ConFilaDuplicada { get; init; }

    /// <summary>Añade una fila de demanda que apunta a un territorio que no existe.</summary>
    public bool ConHechoHuerfano { get; init; }

    /// <summary>Hace que el contrato diga otro número de filas que el fichero.</summary>
    public bool ContratoConFilasDeMas { get; init; }

    /// <summary>Último mes de la release (AAAA-MM).</summary>
    public string Hasta { get; init; } = ReleaseDePrueba.UltimoMes;
}

/// <summary>
/// Crea una release completa (DuckDB, contrato.json y SHA256SUMS) con datos FICTICIOS pero deterministas, para
/// probar sin depender de la red ni de los datos reales. Los valores salen de una fórmula sencilla que los tests
/// también conocen (<see cref="Viajeros"/>), así se puede comprobar cada cifra.
/// </summary>
public static class ReleaseDePrueba
{
    private static readonly JsonSerializerOptions FormatoJson = new() { WriteIndented = true };

    private static readonly string[] FicherosDeSumas = ["contrato.json", "murcia_turismo.duckdb"];

    public const string PrimerMes = "2018-01";
    public const string UltimoMes = "2024-08";

    /// <summary>Peso de cada mes dentro del año (enero a diciembre). Suman 12,0.</summary>
    public static readonly IReadOnlyList<double> Estacionalidad = [0.55, 0.6, 0.8, 0.95, 1.0, 1.2, 1.5, 1.9, 1.3, 0.9, 0.6, 0.7];

    public static readonly string[] Territorios = ["espana", "region-murcia", "costa-calida", "cartagena", "destino-la-manga"];

    /// <summary>Primer mes provisional de los datos del INE (AAAAMM).</summary>
    public const int ProvisionalDesde = 202406;

    /// <summary>Hueco deliberado: apartamentos de la región, residentes en España, abril a junio de 2020 sin dato.</summary>
    public static bool TieneHueco(string territorio, string tipo, string residencia, int anio, int mes) =>
        territorio == "region-murcia" && tipo == "apartamento" && residencia == "espana" && anio == 2020 && mes is >= 4 and <= 6;

    /// <summary>Viajeros ficticios de un mes, o null si ese mes no existe en los datos.</summary>
    public static long? Viajeros(string territorio, string tipo, string residencia, int anio, int mes, double escala = 1)
    {
        if (TieneHueco(territorio, tipo, residencia, anio, mes))
        {
            return null;
        }

        var baseTerritorio = territorio switch
        {
            "region-murcia" => 100_000,
            "costa-calida" => 60_000,
            "cartagena" => 20_000,
            "destino-la-manga" => 8_000,
            _ => throw new ArgumentException("Sin datos de demanda para ese territorio.", nameof(territorio)),
        };

        var factorTipo = tipo == "hotel" ? 1.0 : 0.4;
        var factorResidencia = residencia == "espana" ? 1.0 : 0.5;
        return (long)Math.Round(baseTerritorio * factorTipo * factorResidencia * Estacionalidad[mes - 1] * (1 + (0.05 * (anio - 2018))) * escala);
    }

    public static long Pernoctaciones(string tipo, long viajeros) => viajeros * (tipo == "hotel" ? 3 : 5);

    /// <summary>Los territorios y tipos con demanda; la Manga solo tiene hoteles (y solo hasta diciembre de 2023).</summary>
    public static IEnumerable<(string Territorio, string Tipo)> CombinacionesDeDemanda()
    {
        foreach (var territorio in new[] { "region-murcia", "costa-calida", "cartagena" })
        {
            yield return (territorio, "hotel");
            yield return (territorio, "apartamento");
        }

        yield return ("destino-la-manga", "hotel");
    }

    public static bool ExisteLaMes(string territorio, int fechaId) => territorio != "destino-la-manga" || fechaId <= 202312;

    public static string Fuente(string territorio) => territorio == "destino-la-manga" ? "murciaturistica" : "INE";

    /// <summary>Crea la release en el directorio dado (que debe existir) y devuelve la etiqueta que le corresponde.</summary>
    public static string Crear(string directorio, VarianteDeRelease? variante = null)
    {
        variante ??= new VarianteDeRelease();
        Directory.CreateDirectory(directorio);
        var ruta = Path.Combine(directorio, "murcia_turismo.duckdb");
        if (File.Exists(ruta))
        {
            File.Delete(ruta);
        }

        var meses = MesesHasta(variante.Hasta).ToList();

        using (var conexion = new DuckDBConnection($"Data Source={ruta}"))
        {
            conexion.Open();
            Ejecutar(conexion, "CREATE SCHEMA gold");
            CrearDimensiones(conexion, meses);
            CrearHechos(conexion, meses, variante);
        }

        var contrato = EscribirContrato(directorio, ruta, variante);
        EscribirSumas(directorio);
        return $"datos-{contrato}";
    }

    private static IEnumerable<(int Anio, int Mes, int FechaId)> MesesHasta(string hasta)
    {
        var (anioFin, mesFin) = (int.Parse(hasta[..4], CultureInfo.InvariantCulture), int.Parse(hasta[5..], CultureInfo.InvariantCulture));
        for (var anio = 2018; anio <= anioFin; anio++)
        {
            for (var mes = 1; mes <= 12; mes++)
            {
                if (anio == anioFin && mes > mesFin)
                {
                    yield break;
                }

                yield return (anio, mes, (anio * 100) + mes);
            }
        }
    }

    private static void CrearDimensiones(DuckDBConnection conexion, List<(int Anio, int Mes, int FechaId)> meses)
    {
        Ejecutar(conexion, "CREATE TABLE gold.dim_fecha (fecha_id INTEGER, fecha DATE, anio SMALLINT, mes SMALLINT, trimestre SMALLINT, nombre_mes VARCHAR)");
        Ejecutar(conexion, "INSERT INTO gold.dim_fecha VALUES " + string.Join(", ", meses.Select(m =>
            $"({m.FechaId}, DATE '{m.Anio:0000}-{m.Mes:00}-01', {m.Anio}, {m.Mes}, {((m.Mes - 1) / 3) + 1}, 'mes{m.Mes}')")));

        Ejecutar(conexion, "CREATE TABLE gold.dim_territorio (territorio_id VARCHAR, nombre VARCHAR, nivel VARCHAR, padre_id VARCHAR, fuente VARCHAR, es_desglosado BOOLEAN)");
        Ejecutar(conexion, @"INSERT INTO gold.dim_territorio VALUES
            ('espana', 'España', 'pais', NULL, 'INE', true),
            ('region-murcia', 'Región de Murcia', 'region', 'espana', 'INE', true),
            ('costa-calida', 'Costa Cálida', 'zona_ine', 'region-murcia', 'INE', true),
            ('cartagena', 'Cartagena (municipio)', 'punto_ine', 'region-murcia', 'INE', true),
            ('destino-la-manga', 'La Manga', 'destino_mt', 'region-murcia', 'murciaturistica', true)");

        Ejecutar(conexion, "CREATE TABLE gold.dim_tipo_alojamiento (tipo_alojamiento_id VARCHAR, nombre VARCHAR, encuesta_ine VARCHAR)");
        Ejecutar(conexion, "INSERT INTO gold.dim_tipo_alojamiento VALUES ('hotel', 'Hoteles', 'EOH'), ('apartamento', 'Apartamentos turísticos', 'EOAP'), ('camping', 'Campings', 'EOAC'), ('rural', 'Turismo rural', 'EOTR')");

        Ejecutar(conexion, "CREATE TABLE gold.dim_residencia (residencia_id VARCHAR, nombre VARCHAR)");
        Ejecutar(conexion, "INSERT INTO gold.dim_residencia VALUES ('espana', 'Residentes en España'), ('extranjero', 'Residentes en el extranjero')");
    }

    private static void CrearHechos(DuckDBConnection conexion, List<(int Anio, int Mes, int FechaId)> meses, VarianteDeRelease variante)
    {
        var demanda = new List<string>();
        foreach (var (territorio, tipo) in CombinacionesDeDemanda())
        {
            foreach (var residencia in new[] { "espana", "extranjero" })
            {
                foreach (var (anio, mes, fechaId) in meses)
                {
                    if (!ExisteLaMes(territorio, fechaId) || Viajeros(territorio, tipo, residencia, anio, mes, variante.Escala) is not { } viajeros)
                    {
                        continue;
                    }

                    var provisional = Fuente(territorio) == "INE" && fechaId >= ProvisionalDesde ? "true" : "false";
                    demanda.Add($"({fechaId}, '{territorio}', '{tipo}', '{residencia}', {viajeros}, {Pernoctaciones(tipo, viajeros)}, {provisional}, false, '{Fuente(territorio)}')");
                }
            }
        }

        if (variante.ConFilaDuplicada)
        {
            demanda.Add(demanda[0]);
        }

        if (variante.ConHechoHuerfano)
        {
            demanda.Add("(202401, 'territorio-inexistente', 'hotel', 'espana', 1, 3, false, false, 'INE')");
        }

        var columnaViajeros = variante.SinColumnaViajeros ? string.Empty : "viajeros BIGINT, ";
        Ejecutar(conexion, $"CREATE TABLE gold.fct_demanda_mensual (fecha_id INTEGER, territorio_id VARCHAR, tipo_alojamiento_id VARCHAR, residencia_id VARCHAR, {columnaViajeros}pernoctaciones BIGINT, es_provisional BOOLEAN, tiene_secreto BOOLEAN, fuente VARCHAR)");
        if (variante.SinColumnaViajeros)
        {
            // Insertar sin la columna: se descartan los viajeros de cada tupla.
            demanda = [.. demanda.Select(f => QuitarCampo(f, 4))];
        }

        Insertar(conexion, "gold.fct_demanda_mensual", demanda);

        var oferta = new List<string>();
        foreach (var (anio, mes, fechaId) in meses)
        {
            var provisional = fechaId >= ProvisionalDesde ? "true" : "false";
            var plazas = (long)Math.Round(20_000 + (100 * (anio - 2018)) + (mes * 10) * variante.Escala);
            // Hoteles: plazas y ocupación por plazas; campings: sin plazas (null), con parcelas y su ocupación.
            oferta.Add($"({fechaId}, 'region-murcia', 'hotel', 190, {plazas}, 10000, NULL, NULL, NULL, 2700, {(50 + (10 * Estacionalidad[mes - 1])).ToString("0.00", CultureInfo.InvariantCulture)}, NULL, NULL, NULL, NULL, NULL, NULL, {provisional}, false, 'INE')");
            oferta.Add($"({fechaId}, 'region-murcia', 'camping', 12, NULL, NULL, NULL, 1500, {800 + (mes * 20)}, 90, NULL, NULL, NULL, NULL, NULL, {(30 + (10 * Estacionalidad[mes - 1])).ToString("0.00", CultureInfo.InvariantCulture)}, NULL, {provisional}, false, 'INE')");
        }

        Ejecutar(conexion, @"CREATE TABLE gold.fct_oferta_mensual (fecha_id INTEGER, territorio_id VARCHAR, tipo_alojamiento_id VARCHAR,
            establecimientos BIGINT, plazas BIGINT, habitaciones BIGINT, apartamentos BIGINT, parcelas BIGINT, parcelas_ocupadas BIGINT, personal_empleado BIGINT,
            ocupacion_plazas DOUBLE, ocupacion_plazas_fin_semana DOUBLE, ocupacion_habitaciones DOUBLE, ocupacion_apartamentos DOUBLE, ocupacion_apartamentos_fin_semana DOUBLE,
            ocupacion_parcelas DOUBLE, ocupacion_parcelas_fin_semana DOUBLE, es_provisional BOOLEAN, tiene_secreto BOOLEAN, fuente VARCHAR)");
        Insertar(conexion, "gold.fct_oferta_mensual", oferta);

        var precios = new List<string>();
        foreach (var territorio in new[] { "espana", "region-murcia" })
        {
            foreach (var (anio, mes, fechaId) in meses)
            {
                var indice = 100 + (3 * (anio - 2018)) + mes + (territorio == "espana" ? 5 : 0);
                var variacion = anio == 2018 ? "NULL" : "3.00";
                precios.Add($"({fechaId}, '{territorio}', 'hotel', {indice}.00, {variacion}, {(fechaId >= ProvisionalDesde ? "true" : "false")}, 'INE')");
            }
        }

        Ejecutar(conexion, "CREATE TABLE gold.fct_precios_mensual (fecha_id INTEGER, territorio_id VARCHAR, tipo_alojamiento_id VARCHAR, indice_precios DOUBLE, variacion_interanual DOUBLE, es_provisional BOOLEAN, fuente VARCHAR)");
        Insertar(conexion, "gold.fct_precios_mensual", precios);
    }

    private static string QuitarCampo(string tupla, int indice)
    {
        var campos = tupla.Trim('(', ')').Split(',').Select(c => c.Trim()).ToList();
        campos.RemoveAt(indice);
        return "(" + string.Join(", ", campos) + ")";
    }

    private static void Insertar(DuckDBConnection conexion, string tabla, List<string> filas)
    {
        // En bloques: una sentencia con miles de filas es lenta de analizar.
        foreach (var bloque in filas.Chunk(500))
        {
            Ejecutar(conexion, $"INSERT INTO {tabla} VALUES " + string.Join(", ", bloque));
        }
    }

    private static string EscribirContrato(string directorio, string rutaBase, VarianteDeRelease variante)
    {
        using var conexion = new DuckDBConnection($"Data Source={rutaBase}");
        conexion.Open();

        var tablas = new SortedDictionary<string, object>(StringComparer.Ordinal);
        using (var comando = conexion.CreateCommand())
        {
            comando.CommandText = "SELECT table_name FROM information_schema.tables WHERE table_schema = 'gold' ORDER BY table_name";
            using var lector = comando.ExecuteReader();
            var nombres = new List<string>();
            while (lector.Read())
            {
                nombres.Add(lector.GetString(0));
            }

            foreach (var tabla in nombres)
            {
                var filas = Convert.ToInt64(Escalar(conexion, $"SELECT COUNT(*) FROM gold.{tabla}"), CultureInfo.InvariantCulture);
                var columnas = new List<object>();
                using var c = conexion.CreateCommand();
                c.CommandText = $"SELECT column_name, data_type FROM information_schema.columns WHERE table_schema = 'gold' AND table_name = '{tabla}' ORDER BY ordinal_position";
                using var lc = c.ExecuteReader();
                while (lc.Read())
                {
                    columnas.Add(new { nombre = lc.GetString(0), tipo = lc.GetString(1) });
                }

                tablas[tabla] = new { filas = filas + (variante.ContratoConFilasDeMas && tabla == "fct_demanda_mensual" ? 1 : 0), columnas };
            }
        }

        var contrato = new
        {
            version_contrato = variante.VersionContrato,
            generado = "2024-09-03",
            periodo = new { desde = PrimerMes, hasta = variante.Hasta },
            fuentes = new[] { "INE (datos ficticios de prueba)", "murciaturistica.es (datos ficticios de prueba)" },
            tablas,
        };

        File.WriteAllText(Path.Combine(directorio, "contrato.json"), JsonSerializer.Serialize(contrato, FormatoJson) + "\n");
        return variante.Hasta;
    }

    /// <summary>Calcula SHA256SUMS con el mismo formato que <c>sha256sum</c>.</summary>
    public static void EscribirSumas(string directorio)
    {
        var lineas = FicherosDeSumas
            .Select(f => $"{Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(directorio, f))))}  {f}");
        File.WriteAllText(Path.Combine(directorio, "SHA256SUMS"), string.Join("\n", lineas) + "\n", new UTF8Encoding(false));
    }

    private static void Ejecutar(DuckDBConnection conexion, string sql)
    {
        using var comando = conexion.CreateCommand();
        comando.CommandText = sql;
        comando.ExecuteNonQuery();
    }

    private static object? Escalar(DuckDBConnection conexion, string sql)
    {
        using var comando = conexion.CreateCommand();
        comando.CommandText = sql;
        return comando.ExecuteScalar();
    }
}

namespace MurciaDatos.Datos.Contrato;

/// <summary>
/// Lo que esta API necesita del paquete de datos: las tablas del esquema <c>gold</c> y sus columnas con su tipo.
/// Pueden aparecer columnas o tablas de más (un cambio compatible); si falta algo o cambia un tipo, la
/// versión de datos se rechaza y se sigue sirviendo la anterior.
/// </summary>
public static class ContratoEsperado
{
    /// <summary>Versión mayor del contrato que entiende esta API. Una versión mayor distinta es incompatible.</summary>
    public const int VersionMayor = 1;

    public const string Esquema = "gold";

    public static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Tablas =
        new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["dim_fecha"] = Columnas(("fecha_id", "INTEGER"), ("fecha", "DATE"), ("anio", "SMALLINT"), ("mes", "SMALLINT"), ("trimestre", "SMALLINT")),
            ["dim_territorio"] = Columnas(("territorio_id", "VARCHAR"), ("nombre", "VARCHAR"), ("nivel", "VARCHAR"), ("padre_id", "VARCHAR"), ("fuente", "VARCHAR"), ("es_desglosado", "BOOLEAN")),
            ["dim_tipo_alojamiento"] = Columnas(("tipo_alojamiento_id", "VARCHAR"), ("nombre", "VARCHAR")),
            ["dim_residencia"] = Columnas(("residencia_id", "VARCHAR"), ("nombre", "VARCHAR")),
            ["fct_demanda_mensual"] = Columnas(
                ("fecha_id", "INTEGER"), ("territorio_id", "VARCHAR"), ("tipo_alojamiento_id", "VARCHAR"), ("residencia_id", "VARCHAR"),
                ("viajeros", "BIGINT"), ("pernoctaciones", "BIGINT"), ("es_provisional", "BOOLEAN"), ("tiene_secreto", "BOOLEAN"), ("fuente", "VARCHAR")),
            ["fct_oferta_mensual"] = Columnas(
                ("fecha_id", "INTEGER"), ("territorio_id", "VARCHAR"), ("tipo_alojamiento_id", "VARCHAR"),
                ("establecimientos", "BIGINT"), ("plazas", "BIGINT"), ("habitaciones", "BIGINT"), ("apartamentos", "BIGINT"),
                ("parcelas", "BIGINT"), ("parcelas_ocupadas", "BIGINT"), ("personal_empleado", "BIGINT"),
                ("ocupacion_plazas", "DOUBLE"), ("ocupacion_plazas_fin_semana", "DOUBLE"), ("ocupacion_habitaciones", "DOUBLE"),
                ("ocupacion_apartamentos", "DOUBLE"), ("ocupacion_apartamentos_fin_semana", "DOUBLE"),
                ("ocupacion_parcelas", "DOUBLE"), ("ocupacion_parcelas_fin_semana", "DOUBLE"),
                ("es_provisional", "BOOLEAN"), ("tiene_secreto", "BOOLEAN"), ("fuente", "VARCHAR")),
            ["fct_precios_mensual"] = Columnas(
                ("fecha_id", "INTEGER"), ("territorio_id", "VARCHAR"), ("tipo_alojamiento_id", "VARCHAR"),
                ("indice_precios", "DOUBLE"), ("variacion_interanual", "DOUBLE"), ("es_provisional", "BOOLEAN"), ("fuente", "VARCHAR")),
        };

    private static Dictionary<string, string> Columnas(params (string Nombre, string Tipo)[] columnas) =>
        columnas.ToDictionary(c => c.Nombre, c => c.Tipo, StringComparer.Ordinal);
}

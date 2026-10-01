namespace MurciaDatos.Datos.Consultas;

/// <summary>Las tres tablas de hechos que se sirven.</summary>
public enum Hecho
{
    Demanda,
    Oferta,
    Precios,
}

public enum Agregacion
{
    Mes,
    Trimestre,
    Anio,
}

public sealed record Territorio(string Id, string Nombre, string Nivel, string? PadreId, string Fuente, bool Desglosado);

public sealed record TipoAlojamiento(string Id, string Nombre);

public sealed record Residencia(string Id, string Nombre);

/// <summary>Qué hay realmente en una tabla de hechos: los valores que admite cada filtro y su rango de meses.</summary>
public sealed record DisponibilidadDeHecho(
    IReadOnlySet<string> Territorios,
    IReadOnlySet<string> Tipos,
    IReadOnlySet<string> Residencias,
    int Desde,
    int Hasta,
    int? ProvisionalDesde);

/// <summary>Dimensiones y disponibilidad de una versión de datos, cargadas en memoria al abrirla.</summary>
public sealed class CatalogoDatos(
    IReadOnlyList<Territorio> territorios,
    IReadOnlyList<TipoAlojamiento> tipos,
    IReadOnlyList<Residencia> residencias,
    IReadOnlyDictionary<Hecho, DisponibilidadDeHecho> disponibilidad)
{
    public IReadOnlyList<Territorio> Territorios { get; } = territorios;

    public IReadOnlyList<TipoAlojamiento> Tipos { get; } = tipos;

    public IReadOnlyList<Residencia> Residencias { get; } = residencias;

    public IReadOnlyDictionary<Hecho, DisponibilidadDeHecho> Disponibilidad { get; } = disponibilidad;
}

/// <summary>Cómo se combina una medida cuando varios meses se agrupan en un trimestre o un año.</summary>
public enum TipoDeAgregado
{
    /// <summary>Flujo (viajeros, pernoctaciones): se suma.</summary>
    Suma,

    /// <summary>Existencias, tasas e índices: se hace la media de los meses del periodo.</summary>
    Media,
}

/// <param name="Id">Nombre público de la medida, el que se pide en <c>medidas=</c>.</param>
/// <param name="Columna">Columna de la tabla de hechos. Solo sale de este catálogo cerrado, nunca de la petición.</param>
/// <param name="SoloMensual">Su media no tiene sentido sobre varios meses (una variación interanual), así que solo existe con <c>agregacion=mes</c>.</param>
public sealed record Medida(string Id, string Columna, TipoDeAgregado Agregado, string Unidad, string Descripcion, bool SoloMensual = false);

public static class CatalogoDeMedidas
{
    public static IReadOnlyList<Medida> De(Hecho hecho) => hecho switch
    {
        Hecho.Demanda => Demanda,
        Hecho.Oferta => Oferta,
        Hecho.Precios => Precios,
        _ => throw new ArgumentOutOfRangeException(nameof(hecho)),
    };

    public static string Tabla(Hecho hecho) => hecho switch
    {
        Hecho.Demanda => "gold.fct_demanda_mensual",
        Hecho.Oferta => "gold.fct_oferta_mensual",
        Hecho.Precios => "gold.fct_precios_mensual",
        _ => throw new ArgumentOutOfRangeException(nameof(hecho)),
    };

    private static readonly Medida[] Demanda =
    [
        new("viajeros", "viajeros", TipoDeAgregado.Suma, "personas", "Viajeros alojados"),
        new("pernoctaciones", "pernoctaciones", TipoDeAgregado.Suma, "noches", "Pernoctaciones"),
    ];

    private static readonly Medida[] Oferta =
    [
        new("establecimientos", "establecimientos", TipoDeAgregado.Media, "establecimientos", "Establecimientos abiertos"),
        new("plazas", "plazas", TipoDeAgregado.Media, "plazas", "Plazas ofertadas"),
        new("habitaciones", "habitaciones", TipoDeAgregado.Media, "habitaciones", "Habitaciones"),
        new("apartamentos", "apartamentos", TipoDeAgregado.Media, "apartamentos", "Apartamentos"),
        new("parcelas", "parcelas", TipoDeAgregado.Media, "parcelas", "Parcelas de camping"),
        new("parcelas_ocupadas", "parcelas_ocupadas", TipoDeAgregado.Media, "parcelas", "Parcelas ocupadas"),
        new("personal_empleado", "personal_empleado", TipoDeAgregado.Media, "personas", "Personal empleado"),
        new("ocupacion_plazas", "ocupacion_plazas", TipoDeAgregado.Media, "%", "Grado de ocupación por plazas"),
        new("ocupacion_plazas_fin_semana", "ocupacion_plazas_fin_semana", TipoDeAgregado.Media, "%", "Grado de ocupación por plazas en fin de semana"),
        new("ocupacion_habitaciones", "ocupacion_habitaciones", TipoDeAgregado.Media, "%", "Grado de ocupación por habitaciones"),
        new("ocupacion_apartamentos", "ocupacion_apartamentos", TipoDeAgregado.Media, "%", "Grado de ocupación de apartamentos"),
        new("ocupacion_apartamentos_fin_semana", "ocupacion_apartamentos_fin_semana", TipoDeAgregado.Media, "%", "Grado de ocupación de apartamentos en fin de semana"),
        new("ocupacion_parcelas", "ocupacion_parcelas", TipoDeAgregado.Media, "%", "Grado de ocupación de parcelas"),
        new("ocupacion_parcelas_fin_semana", "ocupacion_parcelas_fin_semana", TipoDeAgregado.Media, "%", "Grado de ocupación de parcelas en fin de semana"),
    ];

    private static readonly Medida[] Precios =
    [
        new("indice_precios", "indice_precios", TipoDeAgregado.Media, "índice", "Índice de precios hoteleros"),
        new("variacion_interanual", "variacion_interanual", TipoDeAgregado.Media, "%", "Variación respecto al mismo mes del año anterior", SoloMensual: true),
    ];
}

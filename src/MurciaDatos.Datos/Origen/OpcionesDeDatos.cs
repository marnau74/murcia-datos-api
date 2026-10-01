using System.ComponentModel.DataAnnotations;

namespace MurciaDatos.Datos.Origen;

public enum TipoDeOrigen
{
    /// <summary>Las releases <c>datos-AAAA-MM</c> de un repositorio de GitHub.</summary>
    GitHub,

    /// <summary>Una carpeta local con los ficheros de una release (desarrollo y pruebas sin red).</summary>
    Directorio,
}

/// <summary>Configuración de la sección <c>Datos</c>.</summary>
public sealed class OpcionesDeDatos
{
    public const string Seccion = "Datos";

    public TipoDeOrigen Origen { get; set; } = TipoDeOrigen.GitHub;

    /// <summary>Repositorio con las releases de datos, como <c>propietario/nombre</c>. Es lo único de donde se descarga.</summary>
    [RegularExpression(@"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")]
    public string Repositorio { get; set; } = "marnau74/murcia-open-data";

    /// <summary>Carpeta con los ficheros de la release, cuando el origen es <see cref="TipoDeOrigen.Directorio"/>.</summary>
    public string? Ruta { get; set; }

    /// <summary>Dónde se guardan las versiones descargadas, para poder arrancar aunque GitHub no responda.</summary>
    public string? Directorio { get; set; }

    [Range(1, 24 * 7)]
    public int IntervaloHoras { get; set; } = 6;

    /// <summary>Tamaño máximo de un fichero descargado; protege el disco si algo publica un fichero enorme.</summary>
    [Range(1, 4096)]
    public int MaxDescargaMb { get; set; } = 256;

    /// <summary>Tiempo máximo de una consulta a DuckDB.</summary>
    [Range(1, 120)]
    public int SegundosMaxConsulta { get; set; } = 5;

    /// <summary>Filas máximas por respuesta; si una consulta devuelve más, se pide acotarla.</summary>
    [Range(1, 1_000_000)]
    public int MaxFilas { get; set; } = 20_000;
}

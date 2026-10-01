namespace MurciaDatos.Datos.Origen;

/// <summary>Nombres de los ficheros de una release de datos del proyecto murcia-open-data.</summary>
public static class FicherosDelPaquete
{
    public const string BaseDeDatos = "murcia_turismo.duckdb";
    public const string Contrato = "contrato.json";
    public const string Sumas = "SHA256SUMS";

    public static readonly IReadOnlyList<string> Todos = [BaseDeDatos, Contrato, Sumas];
}

/// <summary>
/// Una release de datos publicada, todavía sin descargar: su etiqueta (<c>datos-AAAA-MM</c>) y la forma de
/// abrir cada uno de sus ficheros.
/// </summary>
public sealed record PaqueteRemoto(string Etiqueta, Func<string, CancellationToken, Task<Stream>> Abrir);

/// <summary>De dónde salen los datos: las releases de GitHub o, para trabajar sin red, una carpeta local.</summary>
public interface IOrigenDeDatos
{
    /// <summary>La release de datos más reciente, o null si todavía no hay ninguna.</summary>
    Task<PaqueteRemoto?> ObtenerUltimoAsync(CancellationToken cancelacion);
}

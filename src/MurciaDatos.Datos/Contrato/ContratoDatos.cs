using System.Text.Json;
using System.Text.Json.Serialization;

namespace MurciaDatos.Datos.Contrato;

/// <summary>Una columna tal y como la publica el proyecto de datos en <c>contrato.json</c>.</summary>
public sealed record ColumnaContrato(
    [property: JsonPropertyName("nombre")] string Nombre,
    [property: JsonPropertyName("tipo")] string Tipo);

public sealed record TablaContrato(
    [property: JsonPropertyName("filas")] long Filas,
    [property: JsonPropertyName("columnas")] IReadOnlyList<ColumnaContrato> Columnas);

public sealed record PeriodoContrato(
    [property: JsonPropertyName("desde")] string Desde,
    [property: JsonPropertyName("hasta")] string Hasta);

/// <summary>
/// El <c>contrato.json</c> que acompaña a cada release de datos: versión del contrato, periodo cubierto y, de
/// cada tabla, sus filas y el nombre y tipo de sus columnas. Es la promesa del proyecto de datos; la API la
/// comprueba contra lo que espera y contra el propio fichero antes de servir nada.
/// </summary>
public sealed record ContratoDatos(
    [property: JsonPropertyName("version_contrato")] string VersionContrato,
    [property: JsonPropertyName("generado")] string Generado,
    [property: JsonPropertyName("periodo")] PeriodoContrato Periodo,
    [property: JsonPropertyName("fuentes")] IReadOnlyList<string> Fuentes,
    [property: JsonPropertyName("tablas")] IReadOnlyDictionary<string, TablaContrato> Tablas)
{
    public static ContratoDatos Leer(ReadOnlySpan<byte> json)
    {
        try
        {
            return JsonSerializer.Deserialize<ContratoDatos>(json)
                ?? throw new ContratoInvalidoException("contrato.json está vacío.");
        }
        catch (JsonException ex)
        {
            throw new ContratoInvalidoException($"contrato.json no es válido: {ex.Message}");
        }
    }
}

/// <summary>El paquete de datos no cumple el contrato (o el propio contrato está mal formado).</summary>
public sealed class ContratoInvalidoException(string mensaje) : Exception(mensaje);

using System.Text.Json;
using System.Text.Json.Serialization;

namespace MurciaDatos.Explorador.Servicios;

// Los modelos que devuelve la API (solo lo que el explorador necesita). Los nombres son los del JSON de la API.

public sealed record Listado<T>([property: JsonPropertyName("datos")] List<T> Datos);

public sealed record TerritorioApi(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("nombre")] string Nombre,
    [property: JsonPropertyName("nivel")] string Nivel,
    [property: JsonPropertyName("padre")] string? Padre,
    [property: JsonPropertyName("fuente")] string Fuente,
    [property: JsonPropertyName("con_datos_de")] List<string> ConDatosDe);

/// <summary>Un tipo de alojamiento o una residencia, con los recursos para los que hay datos.</summary>
public sealed record OpcionApi(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("nombre")] string Nombre,
    [property: JsonPropertyName("con_datos_de")] List<string> ConDatosDe);

public sealed record MedidaApi(
    [property: JsonPropertyName("recurso")] string Recurso,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("unidad")] string Unidad,
    [property: JsonPropertyName("agregado")] string Agregado,
    [property: JsonPropertyName("descripcion")] string Descripcion,
    [property: JsonPropertyName("solo_mensual")] bool SoloMensual);

public sealed record RangoApi(
    [property: JsonPropertyName("desde")] string Desde,
    [property: JsonPropertyName("hasta")] string Hasta,
    [property: JsonPropertyName("provisional_desde")] string? ProvisionalDesde);

public sealed record MetadatosApi(
    [property: JsonPropertyName("version_datos")] string VersionDatos,
    [property: JsonPropertyName("fuentes")] List<string> Fuentes,
    [property: JsonPropertyName("disponibilidad")] Dictionary<string, RangoApi> Disponibilidad);

public sealed record MetaSerieApi(
    [property: JsonPropertyName("version_datos")] string VersionDatos,
    [property: JsonPropertyName("provisional_desde")] string? ProvisionalDesde,
    [property: JsonPropertyName("total_filas")] int TotalFilas);

/// <summary>Una fila de la respuesta: las claves fijas y una por medida; el valor puede ser null.</summary>
public sealed record RespuestaSerieApi(
    [property: JsonPropertyName("datos")] List<Dictionary<string, JsonElement>> Datos,
    [property: JsonPropertyName("meta")] MetaSerieApi Meta);

/// <summary>Un error de la API en formato problem+json.</summary>
public sealed class ErrorDeApiException(string titulo, string? detalle, IReadOnlyDictionary<string, string[]>? errores) : Exception(detalle ?? titulo)
{
    public string Titulo { get; } = titulo;

    public IReadOnlyDictionary<string, string[]> Errores { get; } = errores ?? new Dictionary<string, string[]>();
}

/// <summary>Todo lo que el explorador necesita saber de los datos para pintar los filtros.</summary>
public sealed record Catalogos(
    MetadatosApi Metadatos,
    List<TerritorioApi> Territorios,
    List<OpcionApi> Tipos,
    List<OpcionApi> Residencias,
    List<MedidaApi> Medidas);

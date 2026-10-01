using System.Text.Json.Serialization;

namespace MurciaDatos.Api.Endpoints;

public sealed record RangoDeMeses(
    [property: JsonPropertyName("desde")] string Desde,
    [property: JsonPropertyName("hasta")] string Hasta,
    [property: JsonPropertyName("provisional_desde")] string? ProvisionalDesde);

public sealed record ControlPublico(
    [property: JsonPropertyName("control")] string Control,
    [property: JsonPropertyName("correcto")] bool Correcto,
    [property: JsonPropertyName("detalle")] string Detalle);

public sealed record Metadatos(
    [property: JsonPropertyName("version_datos")] string VersionDatos,
    [property: JsonPropertyName("huella")] string Huella,
    [property: JsonPropertyName("publicado")] string Publicado,
    [property: JsonPropertyName("cargada_en")] DateTimeOffset CargadaEn,
    [property: JsonPropertyName("version_contrato")] string VersionContrato,
    [property: JsonPropertyName("periodo")] RangoDeMeses Periodo,
    [property: JsonPropertyName("fuentes")] IReadOnlyList<string> Fuentes,
    [property: JsonPropertyName("licencia")] string Licencia,
    [property: JsonPropertyName("disponibilidad")] IReadOnlyDictionary<string, RangoDeMeses> Disponibilidad,
    [property: JsonPropertyName("calidad")] IReadOnlyList<ControlPublico> Calidad,
    [property: JsonPropertyName("ultima_comprobacion")] DateTimeOffset? UltimaComprobacion,
    [property: JsonPropertyName("problema_actualizacion")] string? ProblemaDeActualizacion);

public sealed record TerritorioPublico(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("nombre")] string Nombre,
    [property: JsonPropertyName("nivel")] string Nivel,
    [property: JsonPropertyName("padre")] string? Padre,
    [property: JsonPropertyName("fuente")] string Fuente,
    [property: JsonPropertyName("desglosado")] bool Desglosado,
    [property: JsonPropertyName("con_datos_de")] IReadOnlyList<string> ConDatosDe);

public sealed record TipoPublico(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("nombre")] string Nombre,
    [property: JsonPropertyName("con_datos_de")] IReadOnlyList<string> ConDatosDe);

public sealed record MedidaDeRecurso(
    [property: JsonPropertyName("recurso")] string Recurso,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("unidad")] string Unidad,
    [property: JsonPropertyName("agregado")] string Agregado,
    [property: JsonPropertyName("descripcion")] string Descripcion,
    [property: JsonPropertyName("solo_mensual")] bool SoloMensual);

public sealed record Listado<T>(
    [property: JsonPropertyName("datos")] IReadOnlyList<T> Datos,
    [property: JsonPropertyName("meta")] MetaDeListado Meta);

public sealed record MetaDeListado(
    [property: JsonPropertyName("version_datos")] string VersionDatos);

public sealed record CuotaPublica(
    [property: JsonPropertyName("mes")] int Mes,
    [property: JsonPropertyName("nombre_mes")] string NombreMes,
    [property: JsonPropertyName("cuota_media")] double? CuotaMedia,
    [property: JsonPropertyName("indice")] double? Indice);

public sealed record DatosDeEstacionalidad(
    [property: JsonPropertyName("territorio")] string Territorio,
    [property: JsonPropertyName("tipo")] string Tipo,
    [property: JsonPropertyName("residencia")] string Residencia,
    [property: JsonPropertyName("medida")] string Medida,
    [property: JsonPropertyName("perfil")] IReadOnlyList<CuotaPublica> Perfil,
    [property: JsonPropertyName("relacion_agosto_enero")] double? RelacionAgostoEnero,
    [property: JsonPropertyName("anios_usados")] IReadOnlyList<int> AniosUsados);

public sealed record RespuestaDeEstacionalidad(
    [property: JsonPropertyName("datos")] DatosDeEstacionalidad Datos,
    [property: JsonPropertyName("meta")] MetaDeIndicador Meta);

public sealed record MetaDeIndicador(
    [property: JsonPropertyName("version_datos")] string VersionDatos,
    [property: JsonPropertyName("fuentes")] IReadOnlyList<string> Fuentes,
    [property: JsonPropertyName("nota")] string Nota);

public sealed record ComparacionPublica(
    [property: JsonPropertyName("valor_referencia")] double? ValorReferencia,
    [property: JsonPropertyName("variacion_pct")] double? VariacionPorcentual);

public sealed record PeriodoComparado(
    [property: JsonPropertyName("valor")] double? Valor,
    [property: JsonPropertyName("frente_anio_anterior")] ComparacionPublica FrenteAnioAnterior,
    [property: JsonPropertyName("frente_2019")] ComparacionPublica Frente2019);

public sealed record DatosDeVariacion(
    [property: JsonPropertyName("territorio")] string Territorio,
    [property: JsonPropertyName("tipo")] string Tipo,
    [property: JsonPropertyName("residencia")] string Residencia,
    [property: JsonPropertyName("medida")] string Medida,
    [property: JsonPropertyName("anio")] int? Anio,
    [property: JsonPropertyName("ultimo_mes")] int? UltimoMes,
    [property: JsonPropertyName("provisional")] bool Provisional,
    [property: JsonPropertyName("mes")] PeriodoComparado Mes,
    [property: JsonPropertyName("acumulado")] PeriodoComparado Acumulado);

public sealed record RespuestaDeVariacion(
    [property: JsonPropertyName("datos")] DatosDeVariacion Datos,
    [property: JsonPropertyName("meta")] MetaDeIndicador Meta);

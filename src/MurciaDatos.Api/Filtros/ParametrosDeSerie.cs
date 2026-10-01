using System.ComponentModel;

using Microsoft.AspNetCore.Mvc;

namespace MurciaDatos.Api.Filtros;

/// <summary>Los parámetros de consulta comunes de <c>/v1/demanda</c>, <c>/v1/oferta</c> y <c>/v1/precios</c>.</summary>
/// <remarks>Todos son texto: se validan a mano contra las listas de valores que existen en los datos, para poder responder con los valores permitidos.</remarks>
public sealed record ParametrosDeSerie(
    [property: FromQuery(Name = "territorio"), Description("Identificadores de territorio separados por comas (consulta /v1/territorios). Sin indicar: todos.")] string? Territorio,
    [property: FromQuery(Name = "tipo"), Description("Tipos de alojamiento separados por comas: hotel, apartamento, camping, rural. Sin indicar: todos los que haya.")] string? Tipo,
    [property: FromQuery(Name = "residencia"), Description("Solo demanda. «espana», «extranjero», ambos separados por coma, o «total» para sumarlos. Sin indicar: una fila por residencia.")] string? Residencia,
    [property: FromQuery(Name = "desde"), Description("Primer mes, como AAAA-MM.")] string? Desde,
    [property: FromQuery(Name = "hasta"), Description("Último mes, como AAAA-MM.")] string? Hasta,
    [property: FromQuery(Name = "agregacion"), Description("mes (por defecto), trimestre o anio. Los flujos se suman; las existencias, tasas e índices se promedian.")] string? Agregacion,
    [property: FromQuery(Name = "medidas"), Description("Medidas separadas por comas. Sin indicar: todas las del recurso.")] string? Medidas,
    [property: FromQuery(Name = "formato"), Description("json (por defecto) o csv. También se puede pedir con la cabecera Accept: text/csv.")] string? Formato,
    [property: FromQuery(Name = "excel"), Description("Con formato=csv: true para separar con punto y coma y decimales con coma, y añadir BOM UTF-8 (para abrirlo directamente en Excel en español).")] string? Excel);

/// <summary>Los parámetros de los indicadores derivados.</summary>
public sealed record ParametrosDeIndicador(
    [property: FromQuery(Name = "territorio"), Description("Un territorio con demanda. Por defecto, region-murcia.")] string? Territorio,
    [property: FromQuery(Name = "tipo"), Description("Un tipo de alojamiento. Por defecto, hotel.")] string? Tipo,
    [property: FromQuery(Name = "medida"), Description("viajeros o pernoctaciones (por defecto).")] string? Medida,
    [property: FromQuery(Name = "residencia"), Description("espana, extranjero o total (por defecto).")] string? Residencia,
    [property: FromQuery(Name = "anio"), Description("Solo en la variación: año a analizar. Por defecto, el último con datos.")] string? Anio,
    [property: FromQuery(Name = "incluir_provisionales"), Description("Solo en la estacionalidad: true para que entren también los años con meses provisionales.")] string? IncluirProvisionales);

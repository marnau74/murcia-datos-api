using System.Globalization;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

using MurciaDatos.Api.Cache;
using MurciaDatos.Api.Filtros;
using MurciaDatos.Api.Formatos;
using MurciaDatos.Datos.Actualizacion;
using MurciaDatos.Datos.Consultas;
using MurciaDatos.Datos.Origen;

namespace MurciaDatos.Api.Endpoints;

public static class EndpointsDeDatos
{
    private const string Licencia =
        "Los datos pertenecen a sus autores (INE, Instituto de Turismo de la Región de Murcia y CREM). Esta API publica datos derivados, " +
        "con la atribución correspondiente; consulta las condiciones de reutilización de cada fuente.";

    private static readonly string[] NombresDeMes =
        ["enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre"];

    public static IEndpointRouteBuilder MapearEndpointsDeDatos(this IEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/v1").CacheOutput(PoliticaDeCacheDeDatos.Nombre);

        v1.MapGet("/metadatos", ObtenerMetadatos)
            .WithName("ObtenerMetadatos").WithTags("Catálogos")
            .WithSummary("Versión de los datos, periodo, fuentes, licencia y controles de calidad")
            .WithDescription("Qué datos se están sirviendo ahora: la release de la que salen, el periodo que cubren, desde cuándo son provisionales y el resultado de los controles que esta API ejecuta al cargarlos (suma SHA-256, contrato, esquema, filas, claves e integridad).")
            .Produces<Metadatos>();

        v1.MapGet("/territorios", ListarTerritorios)
            .WithName("ListarTerritorios").WithTags("Catálogos")
            .WithSummary("Territorios, con su jerarquía y los recursos para los que hay datos")
            .WithDescription("Los territorios del INE y de murciaturistica.es conviven en una jerarquía explícita (`padre`). No se suman territorios de niveles o fuentes distintos: un destino de murciaturistica y un municipio del INE no son comparables.")
            .Produces<Listado<TerritorioPublico>>();

        v1.MapGet("/tipos-alojamiento", ListarTipos)
            .WithName("ListarTiposDeAlojamiento").WithTags("Catálogos")
            .WithSummary("Tipos de alojamiento")
            .Produces<Listado<TipoPublico>>();

        v1.MapGet("/residencias", ListarResidencias)
            .WithName("ListarResidencias").WithTags("Catálogos")
            .WithSummary("Residencias de los viajeros (España o extranjero)")
            .WithDescription("En la demanda se puede pedir también `residencia=total`, que suma ambas.")
            .Produces<Listado<TipoPublico>>();

        v1.MapGet("/demanda", (HttpContext http, [AsParameters] ParametrosDeSerie p, AlmacenDeInstantaneas almacen, IOptions<OpcionesDeDatos> o, MetricasDeApi m) => Serie(Hecho.Demanda, "demanda", http, p, almacen, o.Value, m))
            .WithName("ConsultarDemanda").WithTags("Series")
            .WithSummary("Viajeros y pernoctaciones")
            .WithDescription("Demanda de alojamiento turístico por mes, trimestre o año. Los flujos se **suman** al agregar. Un mes sin dato es `null`, nunca 0; con `agregacion` distinta de `mes`, `meses` dice cuántos meses con dato entran en cada periodo (un año en curso tiene menos de 12).")
            .Produces<RespuestaDeSerie>().Produces(StatusCodes.Status304NotModified).ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status429TooManyRequests).ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .Produces<string>(StatusCodes.Status200OK, "text/csv");

        v1.MapGet("/oferta", (HttpContext http, [AsParameters] ParametrosDeSerie p, AlmacenDeInstantaneas almacen, IOptions<OpcionesDeDatos> o, MetricasDeApi m) => Serie(Hecho.Oferta, "oferta", http, p, almacen, o.Value, m))
            .WithName("ConsultarOferta").WithTags("Series")
            .WithSummary("Establecimientos, plazas, ocupación y empleo")
            .WithDescription("Oferta de alojamiento y su ocupación. Son **existencias y tasas**: al agregar por trimestre o año se hace la **media** de los meses. Las medidas que no aplican a un tipo (las parcelas en un hotel) son `null`.")
            .Produces<RespuestaDeSerie>().Produces(StatusCodes.Status304NotModified).ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status429TooManyRequests).ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .Produces<string>(StatusCodes.Status200OK, "text/csv");

        v1.MapGet("/precios", (HttpContext http, [AsParameters] ParametrosDeSerie p, AlmacenDeInstantaneas almacen, IOptions<OpcionesDeDatos> o, MetricasDeApi m) => Serie(Hecho.Precios, "precios", http, p, almacen, o.Value, m))
            .WithName("ConsultarPrecios").WithTags("Series")
            .WithSummary("Índice de precios hoteleros")
            .WithDescription("Índice de precios hoteleros (INE). Al agregar se hace la media del índice; la variación interanual solo existe con `agregacion=mes`.")
            .Produces<RespuestaDeSerie>().Produces(StatusCodes.Status304NotModified).ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status429TooManyRequests).ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .Produces<string>(StatusCodes.Status200OK, "text/csv");

        v1.MapGet("/indicadores/estacionalidad", (HttpContext http, [AsParameters] ParametrosDeIndicador p, AlmacenDeInstantaneas almacen, IOptions<OpcionesDeDatos> o, MetricasDeApi m) => Estacionalidad(http, p, almacen, o.Value, m))
            .WithName("ConsultarEstacionalidad").WithTags("Indicadores")
            .WithSummary("Perfil estacional y relación agosto/enero")
            .WithDescription("Cada año completo (con los doce meses) reparte su total entre los meses; el perfil es la media de esas cuotas. `indice` vale 100 para un mes «medio». Los años con meses provisionales no entran salvo `incluir_provisionales=true`.")
            .Produces<RespuestaDeEstacionalidad>().Produces(StatusCodes.Status304NotModified).ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status429TooManyRequests).ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        v1.MapGet("/indicadores/variacion", (HttpContext http, [AsParameters] ParametrosDeIndicador p, AlmacenDeInstantaneas almacen, IOptions<OpcionesDeDatos> o, MetricasDeApi m) => Variacion(http, p, almacen, o.Value, m))
            .WithName("ConsultarVariacion").WithTags("Indicadores")
            .WithSummary("Variación frente al año anterior y frente a 2019")
            .WithDescription("Del último mes con dato (sin huecos desde enero) y del acumulado del año hasta ese mes, frente al mismo periodo del año anterior y de 2019. Si a la referencia le falta algún mes, la variación es `null`: no se compara contra un periodo incompleto.")
            .Produces<RespuestaDeVariacion>().Produces(StatusCodes.Status304NotModified).ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status429TooManyRequests).ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        // Cualquier otra ruta bajo /v1 es un 404 de la API (no la página del explorador).
        v1.MapGet("/{**ruta}", () => Results.Problem(title: "Recurso no encontrado", detail: "Esa ruta no existe. Consulta /scalar para ver la documentación.", statusCode: StatusCodes.Status404NotFound))
            .ExcludeFromDescription();

        return app;
    }

    // ---- Catálogos ---------------------------------------------------------------------------------------

    private static IResult ObtenerMetadatos(HttpContext http, AlmacenDeInstantaneas almacen, ServicioDeActualizacion actualizacion)
    {
        using var prestamo = almacen.Prestar();
        if (prestamo is null)
        {
            return SinDatos(http);
        }

        var instantanea = prestamo.Instantanea;
        if (NoModificado(http, instantanea) is { } sinCambios)
        {
            return sinCambios;
        }

        var disponibilidad = instantanea.Catalogo.Disponibilidad.ToDictionary(
            d => d.Key.ToString().ToLowerInvariant(),
            d => new RangoDeMeses(ConstructorDeRespuestas.MesComoTexto(d.Value.Desde)!, ConstructorDeRespuestas.MesComoTexto(d.Value.Hasta)!, ConstructorDeRespuestas.MesComoTexto(d.Value.ProvisionalDesde)));

        PonerCabecerasDeCache(http);
        return Results.Json(new Metadatos(
            instantanea.Etiqueta,
            instantanea.Huella,
            instantanea.Contrato.Generado,
            instantanea.CargadaEn,
            instantanea.Contrato.VersionContrato,
            new RangoDeMeses(instantanea.Contrato.Periodo.Desde, instantanea.Contrato.Periodo.Hasta, null),
            instantanea.Contrato.Fuentes,
            Licencia,
            disponibilidad,
            [.. instantanea.Controles.Select(c => new ControlPublico(c.Nombre, c.Correcto, c.Detalle))],
            actualizacion.UltimaComprobacion,
            actualizacion.UltimoProblema));
    }

    private static IResult ListarTerritorios(HttpContext http, AlmacenDeInstantaneas almacen)
    {
        using var prestamo = almacen.Prestar();
        if (prestamo is null)
        {
            return SinDatos(http);
        }

        var instantanea = prestamo.Instantanea;
        if (NoModificado(http, instantanea) is { } sinCambios)
        {
            return sinCambios;
        }

        var disponibilidad = instantanea.Catalogo.Disponibilidad;
        var territorios = instantanea.Catalogo.Territorios.Select(t => new TerritorioPublico(
            t.Id,
            t.Nombre,
            t.Nivel,
            t.PadreId,
            t.Fuente,
            t.Desglosado,
            [.. disponibilidad.Where(d => d.Value.Territorios.Contains(t.Id)).Select(d => d.Key.ToString().ToLowerInvariant())])).ToList();

        PonerCabecerasDeCache(http);
        return Results.Json(new Listado<TerritorioPublico>(territorios, new MetaDeListado(instantanea.Etiqueta)));
    }

    private static IResult ListarTipos(HttpContext http, AlmacenDeInstantaneas almacen) =>
        ListarSimple(http, almacen, c => c.Tipos.Select(t => new TipoPublico(t.Id, t.Nombre)).ToList());

    private static IResult ListarResidencias(HttpContext http, AlmacenDeInstantaneas almacen) =>
        ListarSimple(http, almacen, c => c.Residencias.Select(r => new TipoPublico(r.Id, r.Nombre)).ToList());

    private static IResult ListarSimple(HttpContext http, AlmacenDeInstantaneas almacen, Func<CatalogoDatos, List<TipoPublico>> datos)
    {
        using var prestamo = almacen.Prestar();
        if (prestamo is null)
        {
            return SinDatos(http);
        }

        var instantanea = prestamo.Instantanea;
        if (NoModificado(http, instantanea) is { } sinCambios)
        {
            return sinCambios;
        }

        PonerCabecerasDeCache(http);
        return Results.Json(new Listado<TipoPublico>(datos(instantanea.Catalogo), new MetaDeListado(instantanea.Etiqueta)));
    }

    // ---- Series ------------------------------------------------------------------------------------------

    private static IResult Serie(Hecho hecho, string recurso, HttpContext http, ParametrosDeSerie parametros, AlmacenDeInstantaneas almacen, OpcionesDeDatos opciones, MetricasDeApi metricas)
    {
        using var prestamo = almacen.Prestar();
        if (prestamo is null)
        {
            metricas.Consulta(recurso, "sin_datos");
            return SinDatos(http);
        }

        var instantanea = prestamo.Instantanea;
        var validacion = ValidadorDeConsultas.ValidarSerie(hecho, parametros, http.Request.Query.Keys, http.Request.Headers.Accept.ToString(), instantanea.Catalogo);
        if (!validacion.EsValido)
        {
            metricas.Consulta(recurso, "invalida");
            return Validacion(validacion.Errores);
        }

        if (NoModificado(http, instantanea) is { } sinCambios)
        {
            metricas.Consulta(recurso, "304");
            return sinCambios;
        }

        var (consulta, formato, excel) = validacion.Valor!;
        ResultadoDeSerie resultado;
        var marca = MetricasDeApi.Marca();
        try
        {
            resultado = EjecutarConLimiteDeTiempo(http, prestamo, consulta, opciones);
        }
        catch (OperationCanceledException) when (http.RequestAborted.IsCancellationRequested)
        {
            return Results.Empty;
        }
        catch (OperationCanceledException)
        {
            metricas.Consulta(recurso, "tiempo_agotado");
            return Results.Problem(
                title: "La consulta ha tardado demasiado",
                detail: $"Se ha cancelado tras {opciones.SegundosMaxConsulta} s. Acota la consulta (territorio, tipo, desde/hasta o agregacion).",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        metricas.Duracion(recurso, MetricasDeApi.Desde(marca));

        if (resultado.Truncado)
        {
            metricas.Consulta(recurso, "demasiadas_filas");
            return Results.Problem(
                title: "La consulta devuelve demasiadas filas",
                detail: $"El máximo por respuesta es {opciones.MaxFilas} filas. Acota la consulta (territorio, tipo, desde/hasta) o agrega por trimestre o año.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        metricas.Consulta(recurso, "ok");
        http.Response.Headers.Vary = "Accept";
        PonerCabecerasDeCache(http);

        var filas = ConstructorDeRespuestas.Filas(consulta, resultado.Filas);
        if (formato == FormatoDeSalida.Csv)
        {
            http.Response.Headers.ContentDisposition = $"attachment; filename=\"{recurso}.csv\"";
            return Results.Text(EscritorCsv.Escribir(ConstructorDeRespuestas.Columnas(consulta), filas, excel), "text/csv; charset=utf-8");
        }

        return Results.Json(new RespuestaDeSerie(filas, ConstructorDeRespuestas.Meta(instantanea, consulta, filas.Count)));
    }

    private static ResultadoDeSerie EjecutarConLimiteDeTiempo(HttpContext http, InstantaneaDatos.Prestamo prestamo, ConsultaDeSerie consulta, OpcionesDeDatos opciones)
    {
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(http.RequestAborted);
        limite.CancelAfter(TimeSpan.FromSeconds(opciones.SegundosMaxConsulta));
        return ConsultasDeSeries.Ejecutar(prestamo, consulta, opciones.MaxFilas, limite.Token);
    }

    // ---- Indicadores -------------------------------------------------------------------------------------

    private static IResult Estacionalidad(HttpContext http, ParametrosDeIndicador parametros, AlmacenDeInstantaneas almacen, OpcionesDeDatos opciones, MetricasDeApi metricas) =>
        Indicador(http, parametros, almacen, opciones, metricas, conAnio: false, "estacionalidad", (instantanea, consulta, puntos) =>
        {
            var calculo = Indicadores.CalcularEstacionalidad(puntos, consulta.IncluirProvisionales);
            return new RespuestaDeEstacionalidad(
                new DatosDeEstacionalidad(
                    consulta.Territorio,
                    consulta.Tipo,
                    consulta.Residencia ?? "total",
                    consulta.Medida.Id,
                    [.. calculo.Perfil.Select(c => new CuotaPublica(c.Mes, NombresDeMes[c.Mes - 1], c.CuotaMedia, c.Indice))],
                    calculo.RelacionAgostoEnero,
                    calculo.AniosUsados),
                Meta(instantanea, "Solo entran años completos (doce meses con dato). La cuota es el porcentaje del total anual; el índice vale 100 para un mes medio. La relación se calcula con las cuotas medias."));
        });

    private static IResult Variacion(HttpContext http, ParametrosDeIndicador parametros, AlmacenDeInstantaneas almacen, OpcionesDeDatos opciones, MetricasDeApi metricas) =>
        Indicador(http, parametros, almacen, opciones, metricas, conAnio: true, "variacion", (instantanea, consulta, puntos) =>
        {
            var anio = consulta.Anio ?? puntos.Where(p => p.Valor is not null).Select(p => (int?)p.Anio).Max();
            var calculo = anio is { } a ? Indicadores.CalcularVariacion(puntos, a) : null;

            static ComparacionPublica Publica(Comparacion c) => new(c.ValorReferencia, c.VariacionPorcentual);

            var datos = new DatosDeVariacion(
                consulta.Territorio,
                consulta.Tipo,
                consulta.Residencia ?? "total",
                consulta.Medida.Id,
                anio,
                calculo?.UltimoMes,
                calculo?.Provisional ?? false,
                new PeriodoComparado(calculo?.UltimoMesValor, Publica(calculo?.UltimoMesFrenteAnioAnterior ?? new Comparacion(null, null)), Publica(calculo?.UltimoMesFrente2019 ?? new Comparacion(null, null))),
                new PeriodoComparado(calculo?.AcumuladoValor, Publica(calculo?.AcumuladoFrenteAnioAnterior ?? new Comparacion(null, null)), Publica(calculo?.AcumuladoFrente2019 ?? new Comparacion(null, null))));

            return new RespuestaDeVariacion(
                datos,
                Meta(instantanea, "Se compara el último mes con dato y el acumulado del año hasta ese mes con el mismo periodo del año anterior y de 2019. Si a la referencia le falta algún mes, la variación es null."));
        });

    private static MetaDeIndicador Meta(InstantaneaDatos instantanea, string nota) => new(instantanea.Etiqueta, instantanea.Contrato.Fuentes, nota);

    private static IResult Indicador<T>(
        HttpContext http,
        ParametrosDeIndicador parametros,
        AlmacenDeInstantaneas almacen,
        OpcionesDeDatos opciones,
        MetricasDeApi metricas,
        bool conAnio,
        string recurso,
        Func<InstantaneaDatos, ConsultaDeIndicador, IReadOnlyList<PuntoMensual>, T> calcular)
        where T : notnull
    {
        using var prestamo = almacen.Prestar();
        if (prestamo is null)
        {
            metricas.Consulta(recurso, "sin_datos");
            return SinDatos(http);
        }

        var instantanea = prestamo.Instantanea;
        var validacion = ValidadorDeIndicadores.Validar(parametros, http.Request.Query.Keys, conAnio, instantanea.Catalogo);
        if (!validacion.EsValido)
        {
            metricas.Consulta(recurso, "invalida");
            return Validacion(validacion.Errores);
        }

        if (NoModificado(http, instantanea) is { } sinCambios)
        {
            metricas.Consulta(recurso, "304");
            return sinCambios;
        }

        var consulta = validacion.Valor!;
        var serie = new ConsultaDeSerie(
            Hecho.Demanda,
            [consulta.Territorio],
            [consulta.Tipo],
            consulta.Residencia is null ? [] : [consulta.Residencia],
            consulta.Residencia is null,
            null,
            null,
            Agregacion.Mes,
            [consulta.Medida],
            instantanea.Catalogo.Disponibilidad[Hecho.Demanda].Residencias.Count);

        ResultadoDeSerie resultado;
        var marca = MetricasDeApi.Marca();
        try
        {
            resultado = EjecutarConLimiteDeTiempo(http, prestamo, serie, opciones);
        }
        catch (OperationCanceledException) when (http.RequestAborted.IsCancellationRequested)
        {
            return Results.Empty;
        }
        catch (OperationCanceledException)
        {
            metricas.Consulta(recurso, "tiempo_agotado");
            return Results.Problem(title: "La consulta ha tardado demasiado", statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        metricas.Duracion(recurso, MetricasDeApi.Desde(marca));
        metricas.Consulta(recurso, "ok");
        PonerCabecerasDeCache(http);

        var puntos = resultado.Filas.Select(f => new PuntoMensual(
            int.Parse(f.Periodo[..4], CultureInfo.InvariantCulture),
            int.Parse(f.Periodo[5..], CultureInfo.InvariantCulture),
            f.Valores[0] is { } v ? Convert.ToDouble(v, CultureInfo.InvariantCulture) : null,
            f.Provisional)).ToList();

        return Results.Json(calcular(instantanea, consulta, puntos));
    }

    // ---- Comunes -----------------------------------------------------------------------------------------

    /// <summary>
    /// Pone las cabeceras de caché de una respuesta correcta (ETag y Cache-Control) y, si el cliente ya tiene esa
    /// misma versión (If-None-Match), devuelve 304 sin tocar DuckDB.
    /// </summary>
    private static IResult? NoModificado(HttpContext http, InstantaneaDatos instantanea)
    {
        var etag = ClaveDeConsulta.ETag(instantanea.VersionDeCache, ClaveDeConsulta.De(http.Request));
        http.Items[ClaveEtag] = etag;

        if (ClaveDeConsulta.Coincide(http.Request.Headers.IfNoneMatch.ToString(), etag))
        {
            PonerCabecerasDeCache(http);
            return Results.StatusCode(StatusCodes.Status304NotModified);
        }

        return null;
    }

    /// <summary>
    /// ETag y Cache-Control de una respuesta correcta. Se ponen justo antes de devolverla (y no antes de validar) para
    /// que un 400 o un 503 no lleguen nunca con cabeceras de caché pública.
    /// </summary>
    private static void PonerCabecerasDeCache(HttpContext http)
    {
        if (http.Items[ClaveEtag] is string etag)
        {
            http.Response.Headers.ETag = etag;
            http.Response.Headers.CacheControl = "public, max-age=3600";
        }
    }

    private const string ClaveEtag = "murciadatos.etag";

    private static IResult Validacion(IReadOnlyDictionary<string, string[]> errores) =>
        Results.ValidationProblem(
            errores.ToDictionary(e => e.Key, e => e.Value),
            title: "Parámetros no válidos",
            detail: "Alguno de los parámetros de la consulta no es válido. Cada error indica el parámetro y los valores permitidos.",
            type: "https://httpstatuses.io/400");

    private static IResult SinDatos(HttpContext http)
    {
        http.Response.Headers.RetryAfter = "30";
        return Results.Problem(
            title: "Datos no disponibles",
            detail: "Todavía no se ha cargado ninguna versión de los datos. Inténtalo de nuevo en unos segundos.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}

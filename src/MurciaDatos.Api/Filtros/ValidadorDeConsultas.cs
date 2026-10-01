using System.Globalization;
using System.Text.RegularExpressions;

using MurciaDatos.Datos.Consultas;

namespace MurciaDatos.Api.Filtros;

public enum FormatoDeSalida
{
    Json,
    Csv,
}

public sealed record ConsultaValidada(ConsultaDeSerie Consulta, FormatoDeSalida Formato, bool Excel);

/// <summary>El resultado de validar los parámetros: la consulta lista para ejecutar o los errores por parámetro.</summary>
public sealed class ResultadoDeValidacion<T>
{
    public ResultadoDeValidacion(T valor)
    {
        Valor = valor;
        Errores = new Dictionary<string, string[]>();
    }

    public ResultadoDeValidacion(IReadOnlyDictionary<string, string[]> errores)
    {
        Errores = errores;
    }

    public T? Valor { get; }

    public IReadOnlyDictionary<string, string[]> Errores { get; }

    public bool EsValido => Errores.Count == 0;
}

/// <summary>
/// Valida los parámetros de una consulta contra lo que hay en los datos. Todo valor desconocido da un error que
/// enumera los valores permitidos, y nada de lo que llega por la URL se usa sin pasar por aquí.
/// </summary>
public static partial class ValidadorDeConsultas
{
    public const int MaxLongitudDeParametro = 400;
    public const int MaxElementosPorLista = 50;

    private static readonly string[] ClavesComunes = ["territorio", "tipo", "desde", "hasta", "agregacion", "medidas", "formato", "excel"];

    [GeneratedRegex(@"^\d{4}-(0[1-9]|1[0-2])$")]
    private static partial Regex Mes();

    public static ResultadoDeValidacion<ConsultaValidada> ValidarSerie(
        Hecho hecho,
        ParametrosDeSerie parametros,
        IEnumerable<string> clavesRecibidas,
        string? cabeceraAccept,
        CatalogoDatos catalogo)
    {
        var errores = new Errores();
        var disponibilidad = catalogo.Disponibilidad[hecho];

        var permitidas = hecho == Hecho.Demanda ? [.. ClavesComunes, "residencia"] : ClavesComunes;
        foreach (var clave in clavesRecibidas.Where(c => !permitidas.Contains(c, StringComparer.OrdinalIgnoreCase)))
        {
            errores.Anadir(clave, $"Parámetro desconocido. Parámetros admitidos: {string.Join(", ", permitidas)}.");
        }

        var territorios = Lista(errores, "territorio", parametros.Territorio, disponibilidad.Territorios);
        var tipos = Lista(errores, "tipo", parametros.Tipo, disponibilidad.Tipos);

        var residencias = new List<string>();
        var total = false;
        if (hecho == Hecho.Demanda)
        {
            var permitidasResidencia = disponibilidad.Residencias.Append("total").ToHashSet(StringComparer.Ordinal);
            residencias = Lista(errores, "residencia", parametros.Residencia, permitidasResidencia);
            if (residencias.Contains("total", StringComparer.Ordinal))
            {
                if (residencias.Count > 1)
                {
                    errores.Anadir("residencia", "«total» no se puede combinar con otras residencias.");
                }

                total = true;
                residencias = [];
            }
        }

        var desde = Mensual(errores, "desde", parametros.Desde);
        var hasta = Mensual(errores, "hasta", parametros.Hasta);
        if (desde is { } d && hasta is { } h && d > h)
        {
            errores.Anadir("desde", "«desde» no puede ser posterior a «hasta».");
        }

        var agregacion = Agregacion.Mes;
        if (!string.IsNullOrWhiteSpace(parametros.Agregacion))
        {
            switch (parametros.Agregacion.Trim().ToLowerInvariant())
            {
                case "mes": agregacion = Agregacion.Mes; break;
                case "trimestre": agregacion = Agregacion.Trimestre; break;
                case "anio": agregacion = Agregacion.Anio; break;
                default: errores.Anadir("agregacion", $"Valor desconocido «{Recortar(parametros.Agregacion)}». Permitidos: mes, trimestre, anio."); break;
            }
        }

        var catalogoDeMedidas = CatalogoDeMedidas.De(hecho);
        var medidas = Lista(errores, "medidas", parametros.Medidas, catalogoDeMedidas.Select(m => m.Id).ToHashSet(StringComparer.Ordinal));
        var seleccionadas = medidas.Count == 0
            ? [.. catalogoDeMedidas.Where(m => !m.SoloMensual || agregacion == Agregacion.Mes)]
            : medidas.Select(id => catalogoDeMedidas.Single(m => m.Id == id)).ToList();

        foreach (var medida in seleccionadas.Where(m => m.SoloMensual && agregacion != Agregacion.Mes))
        {
            errores.Anadir("medidas", $"«{medida.Id}» solo está disponible con agregacion=mes: no se puede promediar entre meses.");
        }

        var formato = FormatoDeSalida.Json;
        if (!string.IsNullOrWhiteSpace(parametros.Formato))
        {
            switch (parametros.Formato.Trim().ToLowerInvariant())
            {
                case "json": break;
                case "csv": formato = FormatoDeSalida.Csv; break;
                default: errores.Anadir("formato", $"Valor desconocido «{Recortar(parametros.Formato)}». Permitidos: json, csv."); break;
            }
        }
        else if (PideCsv(cabeceraAccept))
        {
            formato = FormatoDeSalida.Csv;
        }

        var excel = Booleano(errores, "excel", parametros.Excel);
        if (excel && formato != FormatoDeSalida.Csv)
        {
            errores.Anadir("excel", "«excel» solo tiene sentido con formato=csv.");
        }

        if (!errores.EsVacio)
        {
            return new ResultadoDeValidacion<ConsultaValidada>(errores.Resultado());
        }

        var consulta = new ConsultaDeSerie(hecho, territorios, tipos, residencias, total, desde, hasta, agregacion, seleccionadas, disponibilidad.Residencias.Count);
        return new ResultadoDeValidacion<ConsultaValidada>(new ConsultaValidada(consulta, formato, excel));
    }

    /// <summary><c>Accept: text/csv</c> sin pedir también JSON antes: se entrega CSV.</summary>
    public static bool PideCsv(string? accept)
    {
        if (string.IsNullOrWhiteSpace(accept))
        {
            return false;
        }

        var tipos = accept.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Split(';')[0].Trim().ToLowerInvariant());

        foreach (var tipo in tipos)
        {
            if (tipo == "text/csv")
            {
                return true;
            }

            if (tipo is "application/json" or "*/*")
            {
                return false;
            }
        }

        return false;
    }

    internal static List<string> Lista(Errores errores, string nombre, string? texto, IReadOnlySet<string> permitidos)
    {
        var resultado = new List<string>();
        if (texto is null)
        {
            return resultado;
        }

        if (texto.Length > MaxLongitudDeParametro)
        {
            errores.Anadir(nombre, $"Demasiado largo (máximo {MaxLongitudDeParametro} caracteres).");
            return resultado;
        }

        var valores = texto.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(v => v.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (valores.Count == 0)
        {
            errores.Anadir(nombre, "No puede estar vacío: quita el parámetro para no filtrar.");
            return resultado;
        }

        if (valores.Count > MaxElementosPorLista)
        {
            errores.Anadir(nombre, $"Demasiados valores (máximo {MaxElementosPorLista}).");
            return resultado;
        }

        foreach (var valor in valores)
        {
            if (permitidos.Contains(valor))
            {
                resultado.Add(valor);
            }
            else
            {
                errores.Anadir(nombre, $"Valor desconocido «{Recortar(valor)}». Permitidos: {string.Join(", ", permitidos.Order(StringComparer.Ordinal))}.");
            }
        }

        return resultado;
    }

    internal static int? Mensual(Errores errores, string nombre, string? texto)
    {
        if (texto is null)
        {
            return null;
        }

        var limpio = texto.Trim();
        if (!Mes().IsMatch(limpio))
        {
            errores.Anadir(nombre, $"«{Recortar(texto)}» no es un mes válido: el formato es AAAA-MM, por ejemplo 2024-03.");
            return null;
        }

        return (int.Parse(limpio[..4], CultureInfo.InvariantCulture) * 100) + int.Parse(limpio[5..], CultureInfo.InvariantCulture);
    }

    internal static bool Booleano(Errores errores, string nombre, string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return false;
        }

        switch (texto.Trim().ToLowerInvariant())
        {
            case "true" or "1": return true;
            case "false" or "0": return false;
            default:
                errores.Anadir(nombre, $"Valor desconocido «{Recortar(texto)}». Permitidos: true, false.");
                return false;
        }
    }

    internal static string Recortar(string texto) => texto.Length <= 40 ? texto : texto[..40] + "…";

    internal sealed class Errores
    {
        private readonly Dictionary<string, List<string>> _porParametro = new(StringComparer.Ordinal);

        public bool EsVacio => _porParametro.Count == 0;

        public void Anadir(string parametro, string mensaje)
        {
            if (!_porParametro.TryGetValue(parametro, out var lista))
            {
                _porParametro[parametro] = lista = [];
            }

            lista.Add(mensaje);
        }

        public IReadOnlyDictionary<string, string[]> Resultado() =>
            _porParametro.ToDictionary(p => p.Key, p => p.Value.ToArray(), StringComparer.Ordinal);
    }
}

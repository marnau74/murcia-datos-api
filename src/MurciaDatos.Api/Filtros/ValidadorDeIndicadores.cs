using System.Globalization;

using MurciaDatos.Datos.Consultas;

namespace MurciaDatos.Api.Filtros;

/// <summary>Un indicador siempre se calcula para UN territorio, UN tipo y UNA medida: nada de mezclar.</summary>
/// <param name="Residencia">Una residencia concreta, o null para el total de todas.</param>
public sealed record ConsultaDeIndicador(string Territorio, string Tipo, Medida Medida, string? Residencia, int? Anio, bool IncluirProvisionales);

public static class ValidadorDeIndicadores
{
    private static readonly string[] ClavesSinAnio = ["territorio", "tipo", "medida", "residencia", "incluir_provisionales"];
    private static readonly string[] ClavesSinProvisionales = ["territorio", "tipo", "medida", "residencia", "anio"];

    /// <param name="conAnio">true en la variación (admite «anio»); false en la estacionalidad (admite «incluir_provisionales»).</param>
    public static ResultadoDeValidacion<ConsultaDeIndicador> Validar(
        ParametrosDeIndicador parametros,
        IEnumerable<string> clavesRecibidas,
        bool conAnio,
        CatalogoDatos catalogo)
    {
        var errores = new ValidadorDeConsultas.Errores();
        var disponibilidad = catalogo.Disponibilidad[Hecho.Demanda];

        var permitidas = conAnio ? ClavesSinProvisionales : ClavesSinAnio;
        foreach (var clave in clavesRecibidas.Where(c => !permitidas.Contains(c, StringComparer.OrdinalIgnoreCase)))
        {
            errores.Anadir(clave, $"Parámetro desconocido. Parámetros admitidos: {string.Join(", ", permitidas)}.");
        }

        var territorio = Uno(errores, "territorio", parametros.Territorio, "region-murcia", disponibilidad.Territorios);
        var tipo = Uno(errores, "tipo", parametros.Tipo, "hotel", disponibilidad.Tipos);
        var residencia = Uno(errores, "residencia", parametros.Residencia, "total", disponibilidad.Residencias.Append("total").ToHashSet(StringComparer.Ordinal));
        var medidaId = Uno(errores, "medida", parametros.Medida, "pernoctaciones", CatalogoDeMedidas.De(Hecho.Demanda).Select(m => m.Id).ToHashSet(StringComparer.Ordinal));

        int? anio = null;
        if (!string.IsNullOrWhiteSpace(parametros.Anio))
        {
            if (!int.TryParse(parametros.Anio.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var valor) || valor < 1900 || valor > 2200)
            {
                errores.Anadir("anio", $"«{ValidadorDeConsultas.Recortar(parametros.Anio)}» no es un año válido.");
            }
            else
            {
                anio = valor;
            }
        }

        var provisionales = ValidadorDeConsultas.Booleano(errores, "incluir_provisionales", parametros.IncluirProvisionales);

        if (!errores.EsVacio)
        {
            return new ResultadoDeValidacion<ConsultaDeIndicador>(errores.Resultado());
        }

        var medida = CatalogoDeMedidas.De(Hecho.Demanda).Single(m => m.Id == medidaId);
        return new ResultadoDeValidacion<ConsultaDeIndicador>(
            new ConsultaDeIndicador(territorio, tipo, medida, residencia == "total" ? null : residencia, anio, provisionales));
    }

    private static string Uno(ValidadorDeConsultas.Errores errores, string nombre, string? texto, string porDefecto, IReadOnlySet<string> permitidos)
    {
        if (texto is null)
        {
            return porDefecto;
        }

        var lista = ValidadorDeConsultas.Lista(errores, nombre, texto, permitidos);
        if (lista.Count > 1)
        {
            errores.Anadir(nombre, "Solo admite un valor.");
        }

        return lista.Count == 1 ? lista[0] : porDefecto;
    }
}

namespace MurciaDatos.Datos.Consultas;

/// <summary>Un mes de una serie: <c>Valor</c> es null si el mes no tiene dato.</summary>
public sealed record PuntoMensual(int Anio, int Mes, double? Valor, bool Provisional);

public sealed record CuotaMensual(int Mes, double? CuotaMedia, double? Indice);

/// <param name="Perfil">Doce meses; las cuotas son null si no hay ningún año completo.</param>
/// <param name="RelacionAgostoEnero">Cuota media de agosto entre la de enero; null si no se puede calcular.</param>
/// <param name="AniosUsados">Años completos (12 meses con dato) que entran en el cálculo.</param>
public sealed record Estacionalidad(IReadOnlyList<CuotaMensual> Perfil, double? RelacionAgostoEnero, IReadOnlyList<int> AniosUsados);

/// <summary>Una comparación frente a un periodo de referencia; todo es null si la referencia no tiene todos sus meses.</summary>
public sealed record Comparacion(double? ValorReferencia, double? VariacionPorcentual);

/// <param name="Anio">Año analizado.</param>
/// <param name="UltimoMes">Último mes del año con dato, sin huecos desde enero; null si el año no empieza en enero.</param>
/// <param name="Provisional">Algún mes del periodo analizado es provisional.</param>
public sealed record VariacionAnual(
    int Anio,
    int? UltimoMes,
    bool Provisional,
    double? UltimoMesValor,
    Comparacion UltimoMesFrenteAnioAnterior,
    Comparacion UltimoMesFrente2019,
    double? AcumuladoValor,
    Comparacion AcumuladoFrenteAnioAnterior,
    Comparacion AcumuladoFrente2019);

/// <summary>
/// Cálculos sobre una serie mensual ya consultada. Son funciones puras: la regla del proyecto es que un mes sin
/// dato es «sin dato» y nunca un cero, así que ninguna comparación se hace con meses que falten.
/// </summary>
public static class Indicadores
{
    public const int AnioReferencia = 2019;

    /// <summary>
    /// Perfil estacional: cada año completo reparte su total entre los doce meses (cuota); el perfil es la media
    /// de esas cuotas. El índice vale 100 para un mes «medio» (cuota 1/12). Solo entran años con los doce meses.
    /// </summary>
    public static Estacionalidad CalcularEstacionalidad(IReadOnlyList<PuntoMensual> serie, bool incluirProvisionales)
    {
        var anios = serie
            .GroupBy(p => p.Anio)
            .Where(g => g.Count(p => p.Valor is not null) == 12 && g.Select(p => p.Mes).Distinct().Count() == 12)
            .Where(g => incluirProvisionales || g.All(p => !p.Provisional))
            .OrderBy(g => g.Key)
            .ToList();

        var perfil = new List<CuotaMensual>(12);
        var medias = new double?[12];
        for (var mes = 1; mes <= 12; mes++)
        {
            if (anios.Count == 0)
            {
                perfil.Add(new CuotaMensual(mes, null, null));
                continue;
            }

            var cuotas = anios
                .Select(g => (g.First(p => p.Mes == mes).Valor!.Value, Total: g.Sum(p => p.Valor!.Value)))
                .Where(t => t.Total > 0)
                .Select(t => t.Item1 / t.Total)
                .ToList();

            var media = cuotas.Count == 0 ? (double?)null : cuotas.Average();
            medias[mes - 1] = media;
            perfil.Add(new CuotaMensual(mes, media is null ? null : Math.Round(media.Value * 100, 2), media is null ? null : Math.Round(media.Value * 12 * 100, 1)));
        }

        // La relación se calcula con las cuotas sin redondear: redondear antes la desviaría.
        double? relacion = medias[0] is > 0 && medias[7] is not null
            ? Math.Round(medias[7]!.Value / medias[0]!.Value, 2)
            : null;

        return new Estacionalidad(perfil, relacion, anios.Select(g => g.Key).ToList());
    }

    /// <summary>
    /// Variación del último mes con dato y del acumulado del año hasta ese mes, frente al mismo periodo del año
    /// anterior y frente a 2019 (el último año completo previo a la pandemia). Si a la referencia le falta algún
    /// mes del periodo, la comparación es null: no se compara contra un periodo incompleto.
    /// </summary>
    public static VariacionAnual CalcularVariacion(IReadOnlyList<PuntoMensual> serie, int anio)
    {
        var porMes = serie
            .Where(p => p.Valor is not null)
            .ToDictionary(p => (p.Anio, p.Mes), p => p);

        // Último mes del año sin huecos desde enero.
        var ultimo = 0;
        while (ultimo < 12 && porMes.ContainsKey((anio, ultimo + 1)))
        {
            ultimo++;
        }

        if (ultimo == 0)
        {
            var sinDatos = new Comparacion(null, null);
            return new VariacionAnual(anio, null, false, null, sinDatos, sinDatos, null, sinDatos, sinDatos);
        }

        double? ValorDelMes(int a, int m) => porMes.TryGetValue((a, m), out var p) ? p.Valor : null;

        double? Acumulado(int a)
        {
            double total = 0;
            for (var m = 1; m <= ultimo; m++)
            {
                if (ValorDelMes(a, m) is not { } v)
                {
                    return null;
                }

                total += v;
            }

            return total;
        }

        static Comparacion Comparar(double actual, double? referencia) =>
            referencia is > 0
                ? new Comparacion(referencia, Math.Round((actual / referencia.Value - 1) * 100, 2))
                : new Comparacion(referencia, null);

        var ultimoValor = ValorDelMes(anio, ultimo)!.Value;
        var acumulado = Acumulado(anio)!.Value;
        var provisional = Enumerable.Range(1, ultimo).Any(m => porMes[(anio, m)].Provisional);

        return new VariacionAnual(
            anio,
            ultimo,
            provisional,
            ultimoValor,
            Comparar(ultimoValor, ValorDelMes(anio - 1, ultimo)),
            anio == AnioReferencia ? new Comparacion(null, null) : Comparar(ultimoValor, ValorDelMes(AnioReferencia, ultimo)),
            acumulado,
            Comparar(acumulado, Acumulado(anio - 1)),
            anio == AnioReferencia ? new Comparacion(null, null) : Comparar(acumulado, Acumulado(AnioReferencia)));
    }
}

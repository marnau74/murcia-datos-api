using System.Globalization;
using System.Text;

namespace MurciaDatos.Explorador.Logica;

/// <summary>Un punto de una serie. <c>Valor</c> null = sin dato (corta la línea). <c>Incompleto</c>: el periodo agrupa menos meses de los que le tocan.</summary>
public readonly record struct PuntoDeGrafica(string Periodo, double? Valor, bool Incompleto = false, int? Meses = null);

/// <summary>Una serie de la gráfica: su nombre y sus puntos por periodo.</summary>
public sealed record SerieGrafica(string Nombre, IReadOnlyList<PuntoDeGrafica> Puntos);

public sealed record Marca(double Valor, double Y, string Etiqueta);

/// <summary>Los ejes de una gráfica con números «redondos» (1, 2, 5 × 10ⁿ).</summary>
public static class EscalaDeEjes
{
    public static (double Minimo, double Maximo, IReadOnlyList<double> Marcas) Calcular(double minimo, double maximo, bool baseCero, int marcasDeseadas = 5)
    {
        if (baseCero)
        {
            minimo = Math.Min(0, minimo);
            maximo = Math.Max(0, maximo);
        }

        if (maximo - minimo < 1e-9)
        {
            // Una serie plana: se abre un hueco para que la línea quede en medio.
            var relleno = Math.Abs(maximo) < 1e-9 ? 1 : Math.Abs(maximo) * 0.1;
            minimo = baseCero ? Math.Min(0, minimo) : minimo - relleno;
            maximo += relleno;
        }

        var paso = PasoRedondo((maximo - minimo) / Math.Max(1, marcasDeseadas));
        var inicio = Math.Floor(minimo / paso) * paso;
        var fin = Math.Ceiling(maximo / paso) * paso;

        var marcas = new List<double>();
        for (var v = inicio; v <= fin + (paso / 2); v += paso)
        {
            marcas.Add(Math.Round(v, 10));
        }

        return (inicio, fin, marcas);
    }

    internal static double PasoRedondo(double bruto)
    {
        var exponente = Math.Floor(Math.Log10(bruto));
        var fraccion = bruto / Math.Pow(10, exponente);
        var redondo = fraccion switch { <= 1 => 1, <= 2 => 2, <= 5 => 5, _ => 10 };
        return redondo * Math.Pow(10, exponente);
    }
}

/// <summary>La geometría de una gráfica de líneas en coordenadas de SVG: posiciones, ejes y trazos.</summary>
public sealed class GeometriaDeGrafica
{
    public const double Ancho = 760;
    public const double Alto = 340;
    public const double MargenIzquierdo = 64;
    public const double MargenDerecho = 16;
    public const double MargenSuperior = 16;
    public const double MargenInferior = 36;

    private readonly double _minimo;
    private readonly double _maximo;

    public GeometriaDeGrafica(IReadOnlyList<SerieGrafica> series, bool baseCero)
    {
        Series = series;
        Periodos = [.. series.SelectMany(s => s.Puntos.Select(p => p.Periodo)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

        var valores = series.SelectMany(s => s.Puntos).Where(p => p.Valor is not null).Select(p => p.Valor!.Value).ToList();
        HayDatos = valores.Count > 0;
        var (minimo, maximo, marcas) = EscalaDeEjes.Calcular(HayDatos ? valores.Min() : 0, HayDatos ? valores.Max() : 1, baseCero);
        _minimo = minimo;
        _maximo = maximo;
        Marcas = [.. marcas.Select(m => new Marca(m, Y(m), Formato.Corto(m)))];
    }

    public IReadOnlyList<SerieGrafica> Series { get; }

    public IReadOnlyList<string> Periodos { get; }

    public IReadOnlyList<Marca> Marcas { get; }

    public bool HayDatos { get; }

    public double X(int indice) =>
        Periodos.Count <= 1
            ? MargenIzquierdo + ((Ancho - MargenIzquierdo - MargenDerecho) / 2)
            : MargenIzquierdo + (indice * (Ancho - MargenIzquierdo - MargenDerecho) / (Periodos.Count - 1));

    public double Y(double valor) =>
        MargenSuperior + ((Alto - MargenSuperior - MargenInferior) * (1 - ((valor - _minimo) / (_maximo - _minimo))));

    /// <summary>Los periodos que llevan etiqueta en el eje X (unos seis, repartidos).</summary>
    public IReadOnlyList<(double X, string Etiqueta)> EtiquetasX()
    {
        if (Periodos.Count == 0)
        {
            return [];
        }

        var salto = Math.Max(1, (int)Math.Ceiling(Periodos.Count / 6.0));
        return [.. Enumerable.Range(0, Periodos.Count).Where(i => i % salto == 0 || i == Periodos.Count - 1 && (Periodos.Count - 1) % salto >= salto / 2).Select(i => (X(i), Periodos[i]))];
    }

    /// <summary>
    /// El trazo de una serie. Un periodo sin dato corta la línea en dos (<c>M</c> nuevo): no se une un punto con el
    /// siguiente «saltándose» un hueco, que haría creer que el dato existe o que valía cero.
    /// </summary>
    public string Trazo(SerieGrafica serie)
    {
        var indice = Periodos.Select((p, i) => (p, i)).ToDictionary(t => t.p, t => t.i, StringComparer.Ordinal);
        var trazo = new StringBuilder();
        var enLinea = false;

        foreach (var periodo in Periodos)
        {
            var punto = serie.Puntos.FirstOrDefault(p => p.Periodo == periodo);
            if (punto.Valor is not { } valor)
            {
                enLinea = false;
                continue;
            }

            var x = X(indice[periodo]).ToString("0.##", CultureInfo.InvariantCulture);
            var y = Y(valor).ToString("0.##", CultureInfo.InvariantCulture);
            trazo.Append(enLinea ? 'L' : 'M').Append(x).Append(' ').Append(y).Append(' ');
            enLinea = true;
        }

        return trazo.ToString().Trim();
    }

    /// <summary>Los puntos con dato de una serie (para marcar los puntos aislados y mostrar el valor al pasar el ratón).</summary>
    public IEnumerable<(double X, double Y, PuntoDeGrafica Punto)> PuntosConDato(SerieGrafica serie)
    {
        var indice = Periodos.Select((p, i) => (p, i)).ToDictionary(t => t.p, t => t.i, StringComparer.Ordinal);
        foreach (var punto in serie.Puntos)
        {
            if (punto.Valor is { } v)
            {
                yield return (X(indice[punto.Periodo]), Y(v), punto);
            }
        }
    }
}

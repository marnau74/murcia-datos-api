using System.Globalization;

namespace MurciaDatos.Explorador.Logica;

/// <summary>Formato de números a la española (miles con punto, decimales con coma), sin depender de los datos de cultura del navegador.</summary>
public static class Formato
{
    public static string Numero(double valor, int decimales)
    {
        var texto = valor.ToString("N" + decimales.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        return string.Create(texto.Length, texto, static (destino, origen) =>
        {
            for (var i = 0; i < origen.Length; i++)
            {
                destino[i] = origen[i] switch { ',' => '.', '.' => ',', var c => c };
            }
        });
    }

    /// <summary>Un valor con su unidad: enteros sin decimales, tasas con uno, índices con dos.</summary>
    public static string Valor(double? valor, string unidad)
    {
        if (valor is not { } v)
        {
            return "—";
        }

        return unidad switch
        {
            "%" => Numero(v, 1) + " %",
            "índice" => Numero(v, 2),
            _ => Numero(v, Math.Abs(v - Math.Round(v)) < 0.005 ? 0 : 2),
        };
    }

    /// <summary>La versión corta para los ejes: 1,2 M · 340 mil · 45.</summary>
    public static string Corto(double valor)
    {
        var absoluto = Math.Abs(valor);
        if (absoluto >= 1_000_000)
        {
            return Numero(valor / 1_000_000, absoluto % 1_000_000 == 0 ? 0 : 1) + " M";
        }

        if (absoluto >= 10_000)
        {
            return Numero(valor / 1_000, absoluto % 1_000 == 0 ? 0 : 1) + " mil";
        }

        return Numero(valor, Math.Abs(valor - Math.Round(valor)) < 0.005 ? 0 : 1);
    }
}

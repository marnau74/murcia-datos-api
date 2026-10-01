using System.Globalization;

namespace MurciaDatos.Datos.Contrato;

/// <summary>Compara un contrato (el publicado o el leído del propio fichero) con <see cref="ContratoEsperado"/>.</summary>
public static class ValidadorDeContrato
{
    /// <summary>Problemas del contrato publicado; vacío si es compatible.</summary>
    public static IReadOnlyList<string> Validar(ContratoDatos contrato)
    {
        var problemas = new List<string>();

        if (!VersionMayorCompatible(contrato.VersionContrato, out var mayor))
        {
            problemas.Add($"La versión del contrato «{contrato.VersionContrato}» no tiene el formato X.Y.Z.");
        }
        else if (mayor != ContratoEsperado.VersionMayor)
        {
            problemas.Add($"El contrato es de la versión mayor {mayor} y esta API solo entiende la {ContratoEsperado.VersionMayor}.");
        }

        foreach (var (tabla, columnas) in ContratoEsperado.Tablas)
        {
            if (!contrato.Tablas.TryGetValue(tabla, out var publicada))
            {
                problemas.Add($"Falta la tabla {tabla}.");
                continue;
            }

            if (publicada.Filas <= 0)
            {
                problemas.Add($"La tabla {tabla} no tiene filas.");
            }

            var existentes = publicada.Columnas.ToDictionary(c => c.Nombre, c => c.Tipo, StringComparer.Ordinal);
            foreach (var (columna, tipo) in columnas)
            {
                if (!existentes.TryGetValue(columna, out var tipoPublicado))
                {
                    problemas.Add($"Falta la columna {tabla}.{columna}.");
                }
                else if (!string.Equals(tipoPublicado, tipo, StringComparison.OrdinalIgnoreCase))
                {
                    problemas.Add($"La columna {tabla}.{columna} es {tipoPublicado} y se esperaba {tipo}.");
                }
            }
        }

        return problemas;
    }

    internal static bool VersionMayorCompatible(string version, out int mayor)
    {
        mayor = 0;
        var partes = version.Split('.');
        return partes.Length == 3
            && partes.All(p => int.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out _))
            && int.TryParse(partes[0], NumberStyles.None, CultureInfo.InvariantCulture, out mayor);
    }
}

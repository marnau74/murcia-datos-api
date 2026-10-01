using System.Security.Cryptography;

using MurciaDatos.Datos.Contrato;

namespace MurciaDatos.Datos.Actualizacion;

/// <summary>El fichero <c>SHA256SUMS</c> de una release: una línea <c>suma  nombre</c> por fichero.</summary>
public sealed class SumasDeVerificacion
{
    private readonly Dictionary<string, string> _sumas;

    private SumasDeVerificacion(Dictionary<string, string> sumas) => _sumas = sumas;

    public static SumasDeVerificacion Leer(string texto)
    {
        var sumas = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var linea in texto.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // Formato de sha256sum: «suma  nombre» (texto) o «suma *nombre» (binario).
            var separador = linea.IndexOf(' ', StringComparison.Ordinal);
            if (separador != 64)
            {
                throw new ContratoInvalidoException("SHA256SUMS tiene una línea que no es «suma  nombre».");
            }

            var suma = linea[..64].ToLowerInvariant();
            var nombre = linea[65..].TrimStart(' ', '*');
            if (!suma.All(char.IsAsciiHexDigit) || nombre.Length == 0)
            {
                throw new ContratoInvalidoException("SHA256SUMS tiene una línea que no es «suma  nombre».");
            }

            sumas[nombre] = suma;
        }

        return new SumasDeVerificacion(sumas);
    }

    /// <summary>La suma publicada de un fichero, en hexadecimal minúscula.</summary>
    public string De(string fichero) =>
        _sumas.TryGetValue(fichero, out var suma)
            ? suma
            : throw new ContratoInvalidoException($"SHA256SUMS no incluye {fichero}.");

    public static string Calcular(ReadOnlySpan<byte> contenido) => Convert.ToHexStringLower(SHA256.HashData(contenido));
}

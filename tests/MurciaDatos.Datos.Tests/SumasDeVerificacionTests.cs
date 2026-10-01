using MurciaDatos.Datos.Actualizacion;
using MurciaDatos.Datos.Contrato;

using Shouldly;

namespace MurciaDatos.Datos.Tests;

public class SumasDeVerificacionTests
{
    private static readonly string SumaA = new('a', 64);
    private static readonly string SumaB = new('B', 64);

    [Fact]
    public void Lee_el_formato_de_sha256sum_en_modo_texto_y_en_binario()
    {
        var sumas = SumasDeVerificacion.Leer($"{SumaA}  contrato.json\n{SumaB} *murcia_turismo.duckdb\n");

        sumas.De("contrato.json").ShouldBe(SumaA);
        sumas.De("murcia_turismo.duckdb").ShouldBe(SumaB.ToLowerInvariant());
    }

    [Fact]
    public void Tolera_finales_de_linea_de_windows()
    {
        SumasDeVerificacion.Leer($"{SumaA}  contrato.json\r\n").De("contrato.json").ShouldBe(SumaA);
    }

    [Fact]
    public void Un_fichero_que_no_esta_en_la_lista_se_rechaza()
    {
        var sumas = SumasDeVerificacion.Leer($"{SumaA}  contrato.json\n");

        Should.Throw<ContratoInvalidoException>(() => sumas.De("murcia_turismo.duckdb"));
    }

    [Theory]
    [InlineData("esto no es una suma")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz  fichero")]
    [InlineData("aaaa  corta")]
    public void Una_linea_mal_formada_se_rechaza(string linea)
    {
        Should.Throw<ContratoInvalidoException>(() => SumasDeVerificacion.Leer(linea + "\n"));
    }

    [Fact]
    public void Calcula_el_sha256_en_hexadecimal_minuscula()
    {
        // SHA-256 de «abc», del estándar FIPS 180-4.
        SumasDeVerificacion.Calcular("abc"u8).ShouldBe("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
    }
}

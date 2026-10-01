using MurciaDatos.Datos.Consultas;
using MurciaDatos.Tests.Comunes;

using Shouldly;

namespace MurciaDatos.Datos.Tests;

public class IndicadoresTests
{
    private static List<PuntoMensual> Serie(int desde, int hasta, Func<int, int, double?> valor, Func<int, int, bool>? provisional = null)
    {
        var puntos = new List<PuntoMensual>();
        for (var anio = desde; anio <= hasta; anio++)
        {
            for (var mes = 1; mes <= 12; mes++)
            {
                puntos.Add(new PuntoMensual(anio, mes, valor(anio, mes), provisional?.Invoke(anio, mes) ?? false));
            }
        }

        return puntos;
    }

    private static double Crecimiento(int anio) => 1 + (0.05 * (anio - 2018));

    [Fact]
    public void El_perfil_estacional_no_depende_del_nivel_de_cada_anio()
    {
        var serie = Serie(2018, 2023, (a, m) => 1000 * ReleaseDePrueba.Estacionalidad[m - 1] * Crecimiento(a));

        var estacionalidad = Indicadores.CalcularEstacionalidad(serie, incluirProvisionales: false);

        estacionalidad.AniosUsados.ShouldBe([2018, 2019, 2020, 2021, 2022, 2023]);
        for (var mes = 1; mes <= 12; mes++)
        {
            estacionalidad.Perfil[mes - 1].CuotaMedia!.Value.ShouldBe(ReleaseDePrueba.Estacionalidad[mes - 1] / 12 * 100, 0.01);
            estacionalidad.Perfil[mes - 1].Indice!.Value.ShouldBe(ReleaseDePrueba.Estacionalidad[mes - 1] * 100, 0.1);
        }

        estacionalidad.Perfil.Sum(c => c.CuotaMedia!.Value).ShouldBe(100, 0.05);
        estacionalidad.RelacionAgostoEnero.ShouldBe(Math.Round(1.9 / 0.55, 2));
    }

    [Fact]
    public void Un_anio_incompleto_o_con_huecos_no_entra_en_el_perfil()
    {
        var serie = Serie(2018, 2020, (a, m) => a == 2019 && m == 5 ? null : 100 * ReleaseDePrueba.Estacionalidad[m - 1])
            .Where(p => !(p.Anio == 2020 && p.Mes > 8))
            .ToList();

        var estacionalidad = Indicadores.CalcularEstacionalidad(serie, incluirProvisionales: false);

        estacionalidad.AniosUsados.ShouldBe([2018]);
    }

    [Fact]
    public void Los_provisionales_se_excluyen_salvo_que_se_pidan()
    {
        var serie = Serie(2018, 2019, (a, m) => 100 * ReleaseDePrueba.Estacionalidad[m - 1], (a, m) => a == 2019 && m == 12);

        Indicadores.CalcularEstacionalidad(serie, incluirProvisionales: false).AniosUsados.ShouldBe([2018]);
        Indicadores.CalcularEstacionalidad(serie, incluirProvisionales: true).AniosUsados.ShouldBe([2018, 2019]);
    }

    [Fact]
    public void Sin_ningun_anio_completo_el_perfil_no_inventa_cifras()
    {
        var serie = Serie(2024, 2024, (_, m) => 100.0).Where(p => p.Mes <= 8).ToList();

        var estacionalidad = Indicadores.CalcularEstacionalidad(serie, incluirProvisionales: false);

        estacionalidad.AniosUsados.ShouldBeEmpty();
        estacionalidad.Perfil.ShouldAllBe(c => c.CuotaMedia == null && c.Indice == null);
        estacionalidad.RelacionAgostoEnero.ShouldBeNull();
    }

    [Fact]
    public void La_variacion_compara_con_el_anio_anterior_y_con_2019()
    {
        var serie = Serie(2018, 2024, (a, m) => a == 2024 && m > 8 ? null : 100 * ReleaseDePrueba.Estacionalidad[m - 1] * Crecimiento(a));

        var variacion = Indicadores.CalcularVariacion(serie, 2024);

        variacion.UltimoMes.ShouldBe(8);
        variacion.UltimoMesFrenteAnioAnterior.VariacionPorcentual.ShouldBe(Math.Round((Crecimiento(2024) / Crecimiento(2023) - 1) * 100, 2));
        variacion.UltimoMesFrente2019.VariacionPorcentual.ShouldBe(Math.Round((Crecimiento(2024) / Crecimiento(2019) - 1) * 100, 2));

        // El acumulado es enero-agosto frente a enero-agosto: el mismo cociente, porque todos los meses crecen igual.
        variacion.AcumuladoFrenteAnioAnterior.VariacionPorcentual.ShouldBe(variacion.UltimoMesFrenteAnioAnterior.VariacionPorcentual);
        variacion.AcumuladoValor!.Value.ShouldBe(Enumerable.Range(1, 8).Sum(m => 100 * ReleaseDePrueba.Estacionalidad[m - 1] * Crecimiento(2024)), 0.001);
    }

    [Fact]
    public void Si_a_la_referencia_le_falta_un_mes_no_se_compara_contra_un_periodo_incompleto()
    {
        var serie = Serie(2018, 2024, (a, m) => a == 2024 && m > 8 ? null : a == 2023 && m == 3 ? null : 100.0);

        var variacion = Indicadores.CalcularVariacion(serie, 2024);

        variacion.UltimoMesFrenteAnioAnterior.VariacionPorcentual.ShouldBe(0); // agosto sí está en 2023
        variacion.AcumuladoFrenteAnioAnterior.VariacionPorcentual.ShouldBeNull(); // pero marzo de 2023 no
        variacion.AcumuladoFrente2019.VariacionPorcentual.ShouldBe(0);
    }

    [Fact]
    public void El_ultimo_mes_se_toma_sin_huecos_desde_enero()
    {
        var serie = Serie(2018, 2024, (a, m) => a == 2024 && m is > 5 ? null : 100.0);

        Indicadores.CalcularVariacion(serie, 2024).UltimoMes.ShouldBe(5);
    }

    [Fact]
    public void Un_anio_sin_enero_no_tiene_variacion()
    {
        var serie = Serie(2018, 2024, (a, m) => a == 2024 && m == 1 ? null : 100.0);

        var variacion = Indicadores.CalcularVariacion(serie, 2024);

        variacion.UltimoMes.ShouldBeNull();
        variacion.AcumuladoValor.ShouldBeNull();
        variacion.AcumuladoFrenteAnioAnterior.VariacionPorcentual.ShouldBeNull();
    }

    [Fact]
    public void Frente_a_2019_el_propio_2019_no_se_compara_consigo_mismo()
    {
        var serie = Serie(2018, 2019, (a, m) => 100.0);

        var variacion = Indicadores.CalcularVariacion(serie, 2019);

        variacion.UltimoMesFrente2019.VariacionPorcentual.ShouldBeNull();
        variacion.AcumuladoFrente2019.VariacionPorcentual.ShouldBeNull();
        variacion.AcumuladoFrenteAnioAnterior.VariacionPorcentual.ShouldBe(0);
    }

    [Fact]
    public void Una_referencia_en_cero_no_produce_una_division_por_cero()
    {
        var serie = Serie(2018, 2024, (a, m) => a == 2023 ? 0 : 100.0).Where(p => !(p.Anio == 2024 && p.Mes > 3)).ToList();

        var variacion = Indicadores.CalcularVariacion(serie, 2024);

        variacion.UltimoMesFrenteAnioAnterior.VariacionPorcentual.ShouldBeNull();
        variacion.UltimoMesFrente2019.VariacionPorcentual.ShouldBe(0);
    }
}

using MurciaDatos.Datos.Contrato;

using Shouldly;

namespace MurciaDatos.Datos.Tests;

public class ContratoTests
{
    private static ContratoDatos ContratoCompleto(Action<Dictionary<string, TablaContrato>>? cambiar = null, string version = "1.0.0")
    {
        var tablas = ContratoEsperado.Tablas.ToDictionary(
            t => t.Key,
            t => new TablaContrato(10, [.. t.Value.Select(c => new ColumnaContrato(c.Key, c.Value))]));
        cambiar?.Invoke(tablas);
        return new ContratoDatos(version, "2026-09-29", new PeriodoContrato("2015-01", "2026-08"), ["INE"], tablas);
    }

    [Fact]
    public void Un_contrato_con_todo_lo_esperado_es_compatible()
    {
        ValidadorDeContrato.Validar(ContratoCompleto()).ShouldBeEmpty();
    }

    [Fact]
    public void Las_columnas_o_tablas_de_mas_no_rompen_la_compatibilidad()
    {
        var contrato = ContratoCompleto(t =>
        {
            t["dim_fecha"] = t["dim_fecha"] with { Columnas = [.. t["dim_fecha"].Columnas, new ColumnaContrato("nombre_mes", "VARCHAR")] };
            t["tabla_nueva"] = new TablaContrato(1, []);
        });

        ValidadorDeContrato.Validar(contrato).ShouldBeEmpty();
    }

    [Fact]
    public void Una_tabla_que_falta_se_rechaza()
    {
        var contrato = ContratoCompleto(t => t.Remove("fct_precios_mensual"));

        ValidadorDeContrato.Validar(contrato).ShouldContain("Falta la tabla fct_precios_mensual.");
    }

    [Fact]
    public void Una_columna_que_falta_se_rechaza()
    {
        var contrato = ContratoCompleto(t =>
            t["fct_demanda_mensual"] = t["fct_demanda_mensual"] with { Columnas = [.. t["fct_demanda_mensual"].Columnas.Where(c => c.Nombre != "viajeros")] });

        ValidadorDeContrato.Validar(contrato).ShouldContain("Falta la columna fct_demanda_mensual.viajeros.");
    }

    [Fact]
    public void Un_tipo_distinto_se_rechaza()
    {
        var contrato = ContratoCompleto(t =>
            t["fct_demanda_mensual"] = t["fct_demanda_mensual"] with
            {
                Columnas = [.. t["fct_demanda_mensual"].Columnas.Select(c => c.Nombre == "viajeros" ? c with { Tipo = "VARCHAR" } : c)],
            });

        ValidadorDeContrato.Validar(contrato).ShouldContain("La columna fct_demanda_mensual.viajeros es VARCHAR y se esperaba BIGINT.");
    }

    [Fact]
    public void Una_tabla_vacia_se_rechaza()
    {
        var contrato = ContratoCompleto(t => t["dim_territorio"] = t["dim_territorio"] with { Filas = 0 });

        ValidadorDeContrato.Validar(contrato).ShouldContain("La tabla dim_territorio no tiene filas.");
    }

    [Theory]
    [InlineData("2.0.0")]
    [InlineData("0.9.0")]
    public void Otra_version_mayor_del_contrato_es_incompatible(string version)
    {
        ValidadorDeContrato.Validar(ContratoCompleto(version: version)).ShouldHaveSingleItem().ShouldContain("versión mayor");
    }

    [Theory]
    [InlineData("1.4.2", true)]
    [InlineData("1.0.0", true)]
    [InlineData("1.0", false)]
    [InlineData("uno.0.0", false)]
    [InlineData("", false)]
    public void El_formato_de_la_version_se_comprueba(string version, bool valida)
    {
        ValidadorDeContrato.VersionMayorCompatible(version, out _).ShouldBe(valida);
    }

    [Fact]
    public void El_contrato_real_del_proyecto_de_datos_se_lee_con_su_formato()
    {
        const string json = """
            {
              "version_contrato": "1.0.0",
              "generado": "2026-09-29",
              "periodo": { "desde": "2015-01", "hasta": "2026-08" },
              "fuentes": ["INE (EOH)"],
              "tablas": { "dim_residencia": { "filas": 2, "columnas": [ { "nombre": "residencia_id", "tipo": "VARCHAR" } ] } }
            }
            """;

        var contrato = ContratoDatos.Leer(System.Text.Encoding.UTF8.GetBytes(json));

        contrato.Periodo.Hasta.ShouldBe("2026-08");
        contrato.Tablas["dim_residencia"].Filas.ShouldBe(2);
        contrato.Tablas["dim_residencia"].Columnas.ShouldHaveSingleItem().Nombre.ShouldBe("residencia_id");
    }

    [Theory]
    [InlineData("")]
    [InlineData("no es json")]
    [InlineData("null")]
    public void Un_contrato_mal_formado_se_rechaza_con_un_error_claro(string json)
    {
        Should.Throw<ContratoInvalidoException>(() => ContratoDatos.Leer(System.Text.Encoding.UTF8.GetBytes(json)));
    }
}

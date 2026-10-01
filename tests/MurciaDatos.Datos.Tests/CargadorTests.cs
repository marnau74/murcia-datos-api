using MurciaDatos.Datos.Consultas;
using MurciaDatos.Datos.Contrato;
using MurciaDatos.Tests.Comunes;

using Shouldly;

namespace MurciaDatos.Datos.Tests;

public class CargadorTests : IClassFixture<ReleaseCompartida>
{
    private readonly ReleaseCompartida _release;

    public CargadorTests(ReleaseCompartida release) => _release = release;

    [Fact]
    public void Una_release_correcta_pasa_todos_los_controles()
    {
        _release.Instantanea.Controles.ShouldAllBe(c => c.Correcto);
        _release.Instantanea.Controles.Select(c => c.Nombre).ShouldBe(
            ["Contrato compatible", "Sumas SHA-256", "Esquema del fichero", "Filas del contrato", "Claves únicas", "Integridad referencial"]);
    }

    [Fact]
    public void El_catalogo_trae_las_dimensiones_y_lo_que_hay_en_cada_tabla_de_hechos()
    {
        var catalogo = _release.Instantanea.Catalogo;

        catalogo.Territorios.Select(t => t.Id).ShouldBe(["cartagena", "costa-calida", "destino-la-manga", "espana", "region-murcia"], ignoreOrder: true);
        catalogo.Tipos.Select(t => t.Id).ShouldBe(["apartamento", "camping", "hotel", "rural"], ignoreOrder: true);
        catalogo.Residencias.Select(r => r.Id).ShouldBe(["espana", "extranjero"], ignoreOrder: true);

        var demanda = catalogo.Disponibilidad[Hecho.Demanda];
        demanda.Desde.ShouldBe(201801);
        demanda.Hasta.ShouldBe(202408);
        demanda.ProvisionalDesde.ShouldBe(ReleaseDePrueba.ProvisionalDesde);
        demanda.Territorios.ShouldNotContain("espana"); // España solo tiene precios
        demanda.Tipos.ShouldBe(["hotel", "apartamento"], ignoreOrder: true);

        var precios = catalogo.Disponibilidad[Hecho.Precios];
        precios.Territorios.ShouldBe(["espana", "region-murcia"], ignoreOrder: true);
        precios.Residencias.ShouldBeEmpty();
    }

    [Fact]
    public void Si_no_se_verifico_la_suma_se_rechaza()
    {
        var ex = Should.Throw<ContratoInvalidoException>(() => ReleaseCompartida.Abrir(_release.Carpeta, _release.Etiqueta, sumasVerificadas: false));

        ex.Message.ShouldContain("Sumas SHA-256");
    }

    [Fact]
    public void Un_fichero_al_que_le_falta_una_columna_se_rechaza()
    {
        using var directorio = new DirectorioTemporal();
        var carpeta = directorio.Subcarpeta("r");
        var etiqueta = ReleaseDePrueba.Crear(carpeta, new VarianteDeRelease { SinColumnaViajeros = true });

        var ex = Should.Throw<ContratoInvalidoException>(() => ReleaseCompartida.Abrir(carpeta, etiqueta));

        ex.Message.ShouldContain("fct_demanda_mensual.viajeros");
    }

    [Fact]
    public void Una_clave_repetida_se_rechaza()
    {
        using var directorio = new DirectorioTemporal();
        var carpeta = directorio.Subcarpeta("r");
        var etiqueta = ReleaseDePrueba.Crear(carpeta, new VarianteDeRelease { ConFilaDuplicada = true });

        var ex = Should.Throw<ContratoInvalidoException>(() => ReleaseCompartida.Abrir(carpeta, etiqueta));

        ex.Message.ShouldContain("claves repetidas");
    }

    [Fact]
    public void Un_hecho_que_apunta_a_una_dimension_inexistente_se_rechaza()
    {
        using var directorio = new DirectorioTemporal();
        var carpeta = directorio.Subcarpeta("r");
        var etiqueta = ReleaseDePrueba.Crear(carpeta, new VarianteDeRelease { ConHechoHuerfano = true });

        var ex = Should.Throw<ContratoInvalidoException>(() => ReleaseCompartida.Abrir(carpeta, etiqueta));

        ex.Message.ShouldContain("no existen en dim_territorio");
    }

    [Fact]
    public void Si_el_contrato_dice_otro_numero_de_filas_que_el_fichero_se_rechaza()
    {
        using var directorio = new DirectorioTemporal();
        var carpeta = directorio.Subcarpeta("r");
        var etiqueta = ReleaseDePrueba.Crear(carpeta, new VarianteDeRelease { ContratoConFilasDeMas = true });

        var ex = Should.Throw<ContratoInvalidoException>(() => ReleaseCompartida.Abrir(carpeta, etiqueta));

        ex.Message.ShouldContain("el contrato dice");
    }

    [Fact]
    public void Una_version_mayor_distinta_del_contrato_se_rechaza_sin_abrir_el_fichero()
    {
        using var directorio = new DirectorioTemporal();
        var carpeta = directorio.Subcarpeta("r");
        var etiqueta = ReleaseDePrueba.Crear(carpeta, new VarianteDeRelease { VersionContrato = "2.0.0" });

        var ex = Should.Throw<ContratoInvalidoException>(() => ReleaseCompartida.Abrir(carpeta, etiqueta));

        ex.Message.ShouldContain("versión mayor 2");
    }

    [Fact]
    public void Un_fichero_que_no_es_duckdb_se_rechaza_con_un_error_claro()
    {
        using var directorio = new DirectorioTemporal();
        var carpeta = directorio.Subcarpeta("r");
        var etiqueta = ReleaseDePrueba.Crear(carpeta);
        File.WriteAllText(Path.Combine(carpeta, "murcia_turismo.duckdb"), "esto no es una base de datos");

        Should.Throw<ContratoInvalidoException>(() => ReleaseCompartida.Abrir(carpeta, etiqueta));
    }
}

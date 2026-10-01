using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

using MurciaDatos.Datos.Consultas;
using MurciaDatos.Datos.Contrato;
using MurciaDatos.Tests.Comunes;

// Mide el coste de las consultas contra DuckDB (sin la caché de la API, que es lo que se salta casi todo este trabajo).
//
//   dotnet run -c Release --project bench/MurciaDatos.Benchmarks                      → datos de prueba (ficticios)
//   RELEASE_DIR=../murcia-open-data/release dotnet run -c Release --project bench/…  → release real del proyecto de datos
BenchmarkSwitcher.FromAssembly(typeof(ConsultasBenchmarks).Assembly).Run(args);

[MemoryDiagnoser]
public class ConsultasBenchmarks
{
    private DirectorioTemporal? _directorio;
    private InstantaneaDatos _instantanea = null!;

    [GlobalSetup]
    public void Preparar()
    {
        var carpeta = Environment.GetEnvironmentVariable("RELEASE_DIR");
        if (string.IsNullOrWhiteSpace(carpeta))
        {
            _directorio = new DirectorioTemporal();
            carpeta = _directorio.Subcarpeta("release");
            ReleaseDePrueba.Crear(carpeta);
        }

        var contrato = ContratoDatos.Leer(File.ReadAllBytes(Path.Combine(carpeta, "contrato.json")));
        _instantanea = CargadorDeInstantanea.Abrir(Path.Combine(carpeta, "murcia_turismo.duckdb"), null, "datos-bench", "000000000000", contrato, sumasVerificadas: true, TimeProvider.System);
    }

    [GlobalCleanup]
    public void Limpiar()
    {
        _instantanea.Dispose();
        _directorio?.Dispose();
    }

    private static ConsultaDeSerie Demanda(string[] territorios, string[] tipos, Agregacion agregacion, bool total) =>
        new(Hecho.Demanda, territorios, tipos, [], total, null, null, agregacion, CatalogoDeMedidas.De(Hecho.Demanda), NumeroDeResidencias: 2);

    private int Ejecutar(ConsultaDeSerie consulta)
    {
        _instantanea.TryPrestar(out var prestamo).ShouldBeTrue();
        using (prestamo)
        {
            return ConsultasDeSeries.Ejecutar(prestamo, consulta, 100_000, CancellationToken.None).Filas.Count;
        }
    }

    /// <summary>El caso típico del explorador: una serie mensual de un territorio y un tipo.</summary>
    [Benchmark(Baseline = true)]
    public int SerieMensualDeUnTerritorio() => Ejecutar(Demanda(["region-murcia"], ["hotel"], Agregacion.Mes, total: true));

    /// <summary>Un año por territorio y tipo, agregando en la base de datos.</summary>
    [Benchmark]
    public int TodosLosTerritoriosPorAnio() => Ejecutar(Demanda([], [], Agregacion.Anio, total: false));

    /// <summary>La consulta más grande posible: toda la demanda mensual, sin filtrar.</summary>
    [Benchmark]
    public int DemandaCompletaMensual() => Ejecutar(Demanda([], [], Agregacion.Mes, total: false));

    /// <summary>El total de residencias exige una agregación previa por mes (la más cara de las agregadas).</summary>
    [Benchmark]
    public int TotalDeResidenciasPorTrimestre() => Ejecutar(Demanda([], [], Agregacion.Trimestre, total: true));

    /// <summary>Lo que cuesta el indicador de estacionalidad: la consulta mensual más el cálculo en C#.</summary>
    [Benchmark]
    public int Estacionalidad()
    {
        _instantanea.TryPrestar(out var prestamo).ShouldBeTrue();
        using (prestamo)
        {
            var filas = ConsultasDeSeries.Ejecutar(prestamo, Demanda(["region-murcia"], ["hotel"], Agregacion.Mes, total: true) with { Medidas = [.. CatalogoDeMedidas.De(Hecho.Demanda).Where(m => m.Id == "pernoctaciones")] }, 100_000, CancellationToken.None).Filas;
            var puntos = filas.Select(f => new PuntoMensual(int.Parse(f.Periodo[..4], System.Globalization.CultureInfo.InvariantCulture), int.Parse(f.Periodo[5..], System.Globalization.CultureInfo.InvariantCulture), f.Valores[0] is { } v ? Convert.ToDouble(v, System.Globalization.CultureInfo.InvariantCulture) : null, f.Provisional)).ToList();
            return Indicadores.CalcularEstacionalidad(puntos, incluirProvisionales: false).AniosUsados.Count;
        }
    }
}

internal static class Comprobaciones
{
    public static void ShouldBeTrue(this bool valor)
    {
        if (!valor)
        {
            throw new InvalidOperationException("No se ha podido tomar prestada la instantánea.");
        }
    }
}

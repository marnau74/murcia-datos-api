using MurciaDatos.Datos.Consultas;
using MurciaDatos.Datos.Contrato;
using MurciaDatos.Tests.Comunes;

namespace MurciaDatos.Datos.Tests;

/// <summary>Una release de prueba creada una vez y abierta como instantánea, compartida por los tests de una clase.</summary>
public sealed class ReleaseCompartida : IDisposable
{
    private readonly DirectorioTemporal _directorio = new();

    public ReleaseCompartida()
    {
        Carpeta = _directorio.Subcarpeta("release");
        Etiqueta = ReleaseDePrueba.Crear(Carpeta);
        Instantanea = Abrir(Carpeta, Etiqueta);
    }

    public string Carpeta { get; }

    public string Etiqueta { get; }

    public InstantaneaDatos Instantanea { get; }

    public static InstantaneaDatos Abrir(string carpeta, string etiqueta, bool sumasVerificadas = true) =>
        CargadorDeInstantanea.Abrir(
            Path.Combine(carpeta, "murcia_turismo.duckdb"),
            directorioABorrar: null,
            etiqueta,
            huella: "0123456789ab",
            ContratoDatos.Leer(File.ReadAllBytes(Path.Combine(carpeta, "contrato.json"))),
            sumasVerificadas,
            TimeProvider.System);

    public void Dispose()
    {
        Instantanea.Dispose();
        _directorio.Dispose();
    }
}

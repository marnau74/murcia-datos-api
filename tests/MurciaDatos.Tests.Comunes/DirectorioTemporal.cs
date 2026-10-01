namespace MurciaDatos.Tests.Comunes;

/// <summary>Una carpeta temporal única que se borra al terminar (si el sistema lo permite).</summary>
public sealed class DirectorioTemporal : IDisposable
{
    public DirectorioTemporal()
    {
        Ruta = Path.Combine(Path.GetTempPath(), "murcia-datos-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Ruta);
    }

    public string Ruta { get; }

    public string Subcarpeta(string nombre)
    {
        var ruta = Path.Combine(Ruta, nombre);
        Directory.CreateDirectory(ruta);
        return ruta;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Ruta, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

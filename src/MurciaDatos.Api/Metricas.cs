using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace MurciaDatos.Api;

/// <summary>Métricas propias, además de las de ASP.NET Core y el runtime (se exportan con OpenTelemetry).</summary>
public sealed class MetricasDeApi : IDisposable
{
    public const string NombreDelMedidor = "MurciaDatos.Api";

    private readonly Meter _medidor = new(NombreDelMedidor);
    private readonly Counter<long> _consultas;
    private readonly Histogram<double> _duracion;

    public MetricasDeApi()
    {
        _consultas = _medidor.CreateCounter<long>("murciadatos.consultas", description: "Consultas de datos atendidas, por recurso y resultado.");
        _duracion = _medidor.CreateHistogram<double>("murciadatos.consulta.duracion", unit: "ms", description: "Tiempo de ejecución de la consulta en DuckDB (sin contar la caché).");
    }

    public void Consulta(string recurso, string resultado) =>
        _consultas.Add(1, new KeyValuePair<string, object?>("recurso", recurso), new KeyValuePair<string, object?>("resultado", resultado));

    public void Duracion(string recurso, TimeSpan duracion) =>
        _duracion.Record(duracion.TotalMilliseconds, new KeyValuePair<string, object?>("recurso", recurso));

    public static long Marca() => Stopwatch.GetTimestamp();

    public static TimeSpan Desde(long marca) => Stopwatch.GetElapsedTime(marca);

    public void Dispose() => _medidor.Dispose();
}

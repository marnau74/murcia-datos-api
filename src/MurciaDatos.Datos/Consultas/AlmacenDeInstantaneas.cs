namespace MurciaDatos.Datos.Consultas;

/// <summary>
/// Guarda la instantánea que se está sirviendo y la cambia de forma atómica por otra nueva. Las consultas la
/// toman prestada: las que ya estaban en marcha terminan con la versión anterior.
/// </summary>
public sealed class AlmacenDeInstantaneas : IDisposable
{
    private InstantaneaDatos? _actual;

    /// <summary>La versión que se sirve ahora, o null mientras no se haya cargado ninguna.</summary>
    public InstantaneaDatos? Actual => Volatile.Read(ref _actual);

    /// <summary>Toma prestada la instantánea actual; null si todavía no hay datos.</summary>
    public InstantaneaDatos.Prestamo? Prestar()
    {
        // Entre leer la referencia y prestarla se puede haber retirado: se vuelve a leer la nueva.
        for (var intento = 0; intento < 5; intento++)
        {
            var actual = Actual;
            if (actual is null)
            {
                return null;
            }

            if (actual.TryPrestar(out var prestamo))
            {
                return prestamo;
            }
        }

        return null;
    }

    /// <summary>Cambia la instantánea activa y retira la anterior.</summary>
    public void Reemplazar(InstantaneaDatos nueva) => Interlocked.Exchange(ref _actual, nueva)?.Retirar();

    public void Dispose() => Interlocked.Exchange(ref _actual, null)?.Retirar();
}

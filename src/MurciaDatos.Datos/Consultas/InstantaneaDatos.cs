using DuckDB.NET.Data;

using MurciaDatos.Datos.Contrato;

namespace MurciaDatos.Datos.Consultas;

/// <summary>Resultado de una comprobación de la calidad del paquete, tal y como se enseña en <c>/v1/metadatos</c>.</summary>
public sealed record ControlDeCalidad(string Nombre, bool Correcto, string Detalle);

/// <summary>
/// Una versión de los datos abierta en modo solo lectura. Es inmutable: cuando llega otra versión se crea otra
/// instantánea y se cambia la referencia; las peticiones que ya la tenían prestada terminan con ella y el fichero
/// se cierra y se borra cuando se devuelve el último préstamo.
/// </summary>
public sealed class InstantaneaDatos : IDisposable
{
    private readonly Lock _cerrojo = new();
    /// <summary>Hilos que DuckDB puede usar por consulta; ver <see cref="Conectar"/>.</summary>
    internal const int HilosDeDuckDb = 2;

    private const int MaxConexionesEnReposo = 16;

    private readonly DuckDBConnection _ancla;
    private readonly System.Collections.Concurrent.ConcurrentBag<DuckDBConnection> _enReposo = [];
    private readonly string? _directorioABorrar;
    private int _prestamos;
    private bool _retirada;
    private bool _cerrada;

    internal InstantaneaDatos(
        string etiqueta,
        string huella,
        string rutaFichero,
        string? directorioABorrar,
        ContratoDatos contrato,
        CatalogoDatos catalogo,
        IReadOnlyList<ControlDeCalidad> controles,
        DateTimeOffset cargadaEn)
    {
        Etiqueta = etiqueta;
        Huella = huella;
        RutaFichero = rutaFichero;
        _directorioABorrar = directorioABorrar;
        Contrato = contrato;
        Catalogo = catalogo;
        Controles = controles;
        CargadaEn = cargadaEn;

        // Una conexión abierta durante toda la vida de la instantánea mantiene el fichero abierto: las de cada
        // consulta se abren y cierran sin tener que volver a abrir la base de datos.
        _ancla = Conectar(rutaFichero);
    }

    /// <summary>Etiqueta de la release, como <c>datos-2026-09</c>.</summary>
    public string Etiqueta { get; }

    /// <summary>Primeros caracteres del SHA-256 del fichero: distingue dos publicaciones con la misma etiqueta.</summary>
    public string Huella { get; }

    public string RutaFichero { get; }

    public ContratoDatos Contrato { get; }

    public CatalogoDatos Catalogo { get; }

    public IReadOnlyList<ControlDeCalidad> Controles { get; }

    public DateTimeOffset CargadaEn { get; }

    /// <summary>Identifica estos datos para las cabeceras de caché: cambia si cambian los datos.</summary>
    public string VersionDeCache => $"{Etiqueta}-{Huella}";

    internal static DuckDBConnection Conectar(string ruta)
    {
        // Los datos son pequeños (miles de filas): con todos los hilos de la máquina el coste de repartir el trabajo
        // supera al de hacerlo (190 ms frente a 3 ms en las mediciones del README), así que se limita a dos.
        var conexion = new DuckDBConnection($"Data Source={ruta};access_mode=READ_ONLY;threads={HilosDeDuckDb}");
        conexion.Open();
        return conexion;
    }

    /// <summary>Toma prestada la instantánea para una consulta. Devuelve false si ya está retirada.</summary>
    public bool TryPrestar(out Prestamo prestamo)
    {
        lock (_cerrojo)
        {
            if (_retirada)
            {
                prestamo = default!;
                return false;
            }

            _prestamos++;
        }

        prestamo = new Prestamo(this);
        return true;
    }

    /// <summary>Se deja de servir: se libera en cuanto no quede ninguna consulta usándola.</summary>
    public void Retirar()
    {
        bool cerrarYa;
        lock (_cerrojo)
        {
            _retirada = true;
            cerrarYa = _prestamos == 0;
        }

        if (cerrarYa)
        {
            Cerrar();
        }
    }

    public void Dispose() => Retirar();

    private void Devolver()
    {
        bool cerrarYa;
        lock (_cerrojo)
        {
            _prestamos--;
            cerrarYa = _retirada && _prestamos == 0;
        }

        if (cerrarYa)
        {
            Cerrar();
        }
    }

    private ConexionPrestada TomarConexion() => new(this, _enReposo.TryTake(out var conexion) ? conexion : Conectar(RutaFichero));

    private void DevolverConexion(DuckDBConnection conexion)
    {
        // Si ya está retirada o el grupo está lleno, la conexión se cierra en lugar de guardarla.
        if (!_retirada && _enReposo.Count < MaxConexionesEnReposo)
        {
            _enReposo.Add(conexion);
            return;
        }

        conexion.Dispose();
    }

    private void Cerrar()
    {
        lock (_cerrojo)
        {
            if (_cerrada)
            {
                return;
            }

            _cerrada = true;
        }

        while (_enReposo.TryTake(out var conexion))
        {
            conexion.Dispose();
        }

        _ancla.Dispose();

        if (_directorioABorrar is not null)
        {
            try
            {
                Directory.Delete(_directorioABorrar, recursive: true);
            }
            catch (IOException)
            {
                // Se limpia en el próximo arranque o la próxima actualización.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>Uso de la instantánea durante una consulta. Hay que devolverla (Dispose) al terminar.</summary>
    public sealed class Prestamo : IDisposable
    {
        private InstantaneaDatos? _instantanea;

        internal Prestamo(InstantaneaDatos instantanea) => _instantanea = instantanea;

        public InstantaneaDatos Instantanea => _instantanea ?? throw new ObjectDisposedException(nameof(Prestamo));

        /// <summary>
        /// Una conexión para una consulta, de un grupo de conexiones ya abiertas (abrir una cuesta más que ejecutar una
        /// consulta pequeña). Hay que devolverla (Dispose) al terminar.
        /// </summary>
        public ConexionPrestada AbrirConexion() => Instantanea.TomarConexion();

        public void Dispose() => Interlocked.Exchange(ref _instantanea, null)?.Devolver();
    }

    /// <summary>Una conexión sacada del grupo; al devolverla vuelve a él para la siguiente consulta.</summary>
    public sealed class ConexionPrestada : IDisposable
    {
        private readonly InstantaneaDatos _origen;
        private DuckDBConnection? _conexion;

        internal ConexionPrestada(InstantaneaDatos origen, DuckDBConnection conexion)
        {
            _origen = origen;
            _conexion = conexion;
        }

        public DuckDBConnection Conexion => _conexion ?? throw new ObjectDisposedException(nameof(ConexionPrestada));

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _conexion, null) is { } conexion)
            {
                _origen.DevolverConexion(conexion);
            }
        }
    }
}

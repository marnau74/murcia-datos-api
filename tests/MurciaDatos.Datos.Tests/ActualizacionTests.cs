using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using MurciaDatos.Datos.Actualizacion;
using MurciaDatos.Datos.Consultas;
using MurciaDatos.Datos.Origen;
using MurciaDatos.Tests.Comunes;

using Shouldly;

namespace MurciaDatos.Datos.Tests;

public sealed class ActualizacionTests : IDisposable
{
    private readonly DirectorioTemporal _directorio = new();
    private readonly AlmacenDeInstantaneas _almacen = new();
    private readonly OrigenFalso _origen = new();
    private readonly ObservadorFalso _observador = new();
    private readonly ServicioDeActualizacion _servicio;
    private readonly OpcionesDeDatos _opciones;

    public ActualizacionTests()
    {
        _opciones = new OpcionesDeDatos { Directorio = _directorio.Subcarpeta("datos") };
        _servicio = Crear(_almacen, _origen);
    }

    private ServicioDeActualizacion Crear(AlmacenDeInstantaneas almacen, IOrigenDeDatos origen) =>
        new(origen, almacen, [_observador], Options.Create(_opciones), TimeProvider.System, NullLogger<ServicioDeActualizacion>.Instance);

    private string NuevaRelease(VarianteDeRelease? variante = null)
    {
        var carpeta = _directorio.Subcarpeta($"release-{Guid.NewGuid():N}");
        var etiqueta = ReleaseDePrueba.Crear(carpeta, variante);
        _origen.Etiqueta = etiqueta;
        _origen.Carpeta = carpeta;
        return carpeta;
    }

    private static long Viajeros(InstantaneaDatos.Prestamo prestamo)
    {
        var consulta = new ConsultaDeSerie(
            Hecho.Demanda, ["region-murcia"], ["hotel"], ["espana"], false, 202401, 202401, Agregacion.Mes,
            [.. CatalogoDeMedidas.De(Hecho.Demanda).Where(m => m.Id == "viajeros")]);
        return Convert.ToInt64(ConsultasDeSeries.Ejecutar(prestamo, consulta, 10, TestContext.Current.CancellationToken).Filas.Single().Valores[0], System.Globalization.CultureInfo.InvariantCulture);
    }

    private long ViajerosServidos()
    {
        using var prestamo = _almacen.Prestar()!;
        return Viajeros(prestamo);
    }

    private static readonly long Base = ReleaseDePrueba.Viajeros("region-murcia", "hotel", "espana", 2024, 1)!.Value;

    [Fact]
    public async Task La_primera_actualizacion_carga_los_datos_y_avisa_a_los_observadores()
    {
        NuevaRelease();

        var resultado = await _servicio.ActualizarAsync(TestContext.Current.CancellationToken);

        resultado.ShouldBe(ResultadoDeActualizacion.Actualizado);
        _almacen.Actual.ShouldNotBeNull().Etiqueta.ShouldBe("datos-2024-08");
        _almacen.Actual.Controles.ShouldAllBe(c => c.Correcto);
        ViajerosServidos().ShouldBe(Base);
        _observador.Avisos.ShouldBe(1);
        _servicio.UltimoProblema.ShouldBeNull();
    }

    [Fact]
    public async Task Si_no_hay_nada_nuevo_no_vuelve_a_descargar_ni_a_avisar()
    {
        NuevaRelease();
        await _servicio.ActualizarAsync(TestContext.Current.CancellationToken);
        var descargasAntes = _origen.Descargas;

        var resultado = await _servicio.ActualizarAsync(TestContext.Current.CancellationToken);

        resultado.ShouldBe(ResultadoDeActualizacion.SinNovedades);
        _origen.Descargas.ShouldBe(descargasAntes);
        _observador.Avisos.ShouldBe(1);
    }

    [Fact]
    public async Task Una_version_nueva_sustituye_a_la_anterior_sin_cortes()
    {
        NuevaRelease();
        await _servicio.ActualizarAsync(TestContext.Current.CancellationToken);
        using var enCurso = _almacen.Prestar()!; // una consulta que empezó con la versión vieja

        NuevaRelease(new VarianteDeRelease { Escala = 2, Hasta = "2024-09" });
        var resultado = await _servicio.ActualizarAsync(TestContext.Current.CancellationToken);

        resultado.ShouldBe(ResultadoDeActualizacion.Actualizado);
        _almacen.Actual!.Etiqueta.ShouldBe("datos-2024-09");
        ViajerosServidos().ShouldBe(Base * 2);
        Viajeros(enCurso).ShouldBe(Base); // la consulta en curso termina con su versión
        _observador.Avisos.ShouldBe(2);
    }

    [Fact]
    public async Task Una_republicacion_con_la_misma_etiqueta_pero_otros_datos_tambien_se_carga()
    {
        // El pipeline mensual repite la release del mes con --clobber: misma etiqueta, otro contenido.
        NuevaRelease();
        await _servicio.ActualizarAsync(TestContext.Current.CancellationToken);
        var huellaAntes = _almacen.Actual!.Huella;

        NuevaRelease(new VarianteDeRelease { Escala = 3 });
        var resultado = await _servicio.ActualizarAsync(TestContext.Current.CancellationToken);

        resultado.ShouldBe(ResultadoDeActualizacion.Actualizado);
        _almacen.Actual!.Huella.ShouldNotBe(huellaAntes);
        ViajerosServidos().ShouldBe(Base * 3);
    }

    [Fact]
    public async Task Una_release_mas_antigua_que_la_actual_se_ignora()
    {
        NuevaRelease(new VarianteDeRelease { Hasta = "2024-08" });
        await _servicio.ActualizarAsync(TestContext.Current.CancellationToken);

        NuevaRelease(new VarianteDeRelease { Hasta = "2024-06", Escala = 9 });
        var resultado = await _servicio.ActualizarAsync(TestContext.Current.CancellationToken);

        resultado.ShouldBe(ResultadoDeActualizacion.SinNovedades);
        ViajerosServidos().ShouldBe(Base);
    }

    [Fact]
    public async Task Si_la_suma_del_fichero_no_coincide_se_rechaza_y_se_sigue_con_la_anterior()
    {
        NuevaRelease();
        await _servicio.ActualizarAsync(TestContext.Current.CancellationToken);

        var carpeta = NuevaRelease(new VarianteDeRelease { Escala = 2, Hasta = "2024-09" });
        // Se estropea el fichero DESPUÉS de calcular las sumas: justo lo que detecta SHA256SUMS.
        await using (var f = new FileStream(Path.Combine(carpeta, "murcia_turismo.duckdb"), FileMode.Open, FileAccess.Write))
        {
            f.Seek(2000, SeekOrigin.Begin);
            f.WriteByte(0x7F);
        }

        var resultado = await _servicio.ActualizarAsync(TestContext.Current.CancellationToken);

        resultado.ShouldBe(ResultadoDeActualizacion.Rechazado);
        _servicio.UltimoProblema.ShouldNotBeNull().ShouldContain("SHA-256");
        _almacen.Actual!.Etiqueta.ShouldBe("datos-2024-08");
        ViajerosServidos().ShouldBe(Base);
        _observador.Avisos.ShouldBe(1);
        Directory.GetDirectories(_opciones.Directorio!, "descarga-*").ShouldBeEmpty();
    }

    [Fact]
    public async Task Un_contrato_roto_se_rechaza_y_no_deja_rastro_en_disco()
    {
        NuevaRelease();
        await _servicio.ActualizarAsync(TestContext.Current.CancellationToken);

        NuevaRelease(new VarianteDeRelease { SinColumnaViajeros = true, Hasta = "2024-09" });
        var resultado = await _servicio.ActualizarAsync(TestContext.Current.CancellationToken);

        resultado.ShouldBe(ResultadoDeActualizacion.Rechazado);
        _servicio.UltimoProblema.ShouldNotBeNull().ShouldContain("viajeros");
        ViajerosServidos().ShouldBe(Base);
        Directory.GetDirectories(_opciones.Directorio!).Select(Path.GetFileName).ShouldAllBe(n => n!.StartsWith("datos-2024-08", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Una_version_mayor_incompatible_del_contrato_se_rechaza()
    {
        NuevaRelease(new VarianteDeRelease { VersionContrato = "2.0.0" });

        var resultado = await _servicio.ActualizarAsync(TestContext.Current.CancellationToken);

        resultado.ShouldBe(ResultadoDeActualizacion.Rechazado);
        _almacen.Actual.ShouldBeNull();
    }

    [Fact]
    public async Task Un_fichero_que_se_pasa_del_tamano_maximo_se_corta()
    {
        NuevaRelease();
        _opciones.MaxDescargaMb = 1;
        _origen.FicheroGigante = true;

        var resultado = await _servicio.ActualizarAsync(TestContext.Current.CancellationToken);

        resultado.ShouldBe(ResultadoDeActualizacion.Rechazado);
        _servicio.UltimoProblema.ShouldNotBeNull().ShouldContain("tamaño máximo");
    }

    [Fact]
    public async Task Sin_ninguna_release_publicada_no_hay_datos_y_se_dice()
    {
        var resultado = await _servicio.ActualizarAsync(TestContext.Current.CancellationToken);

        resultado.ShouldBe(ResultadoDeActualizacion.SinOrigen);
        _almacen.Actual.ShouldBeNull();
    }

    [Fact]
    public async Task Un_fallo_de_red_se_anota_y_se_sigue_sirviendo_lo_que_habia()
    {
        NuevaRelease();
        await _servicio.ActualizarAsync(TestContext.Current.CancellationToken);

        _origen.Fallo = new HttpRequestException("sin red");
        var resultado = await _servicio.ActualizarAsync(TestContext.Current.CancellationToken);

        resultado.ShouldBe(ResultadoDeActualizacion.Fallido);
        _servicio.UltimoProblema.ShouldBe("sin red");
        ViajerosServidos().ShouldBe(Base);
    }

    [Fact]
    public async Task Al_arrancar_carga_lo_guardado_en_disco_aunque_el_origen_no_responda()
    {
        NuevaRelease();
        await _servicio.ActualizarAsync(TestContext.Current.CancellationToken);

        using var almacenNuevo = new AlmacenDeInstantaneas();
        _origen.Fallo = new HttpRequestException("sin red");
        using var servicioNuevo = Crear(almacenNuevo, _origen);

        await servicioNuevo.CargarDeDiscoAsync(TestContext.Current.CancellationToken);

        almacenNuevo.Actual.ShouldNotBeNull().Etiqueta.ShouldBe("datos-2024-08");
        using var prestamo = almacenNuevo.Prestar()!;
        Viajeros(prestamo).ShouldBe(Base);
    }

    [Fact]
    public async Task Un_fichero_de_disco_manipulado_no_se_carga()
    {
        NuevaRelease();
        await _servicio.ActualizarAsync(TestContext.Current.CancellationToken);
        _almacen.Dispose();

        var carpeta = Directory.GetDirectories(_opciones.Directorio!, "datos-*").Single();
        await using (var f = new FileStream(Path.Combine(carpeta, "murcia_turismo.duckdb"), FileMode.Open, FileAccess.Write))
        {
            f.Seek(2000, SeekOrigin.Begin);
            f.WriteByte(0x7F);
        }

        using var almacenNuevo = new AlmacenDeInstantaneas();
        using var servicioNuevo = Crear(almacenNuevo, _origen);
        await servicioNuevo.CargarDeDiscoAsync(TestContext.Current.CancellationToken);

        almacenNuevo.Actual.ShouldBeNull();
    }

    [Fact]
    public async Task Solo_se_conservan_en_disco_la_version_nueva_y_la_anterior()
    {
        foreach (var hasta in new[] { "2024-05", "2024-06", "2024-07", "2024-08" })
        {
            NuevaRelease(new VarianteDeRelease { Hasta = hasta });
            (await _servicio.ActualizarAsync(TestContext.Current.CancellationToken)).ShouldBe(ResultadoDeActualizacion.Actualizado);
        }

        var carpetas = Directory.GetDirectories(_opciones.Directorio!, "datos-*").Select(c => Path.GetFileName(c)!).ToList();

        carpetas.Count.ShouldBeLessThanOrEqualTo(3); // la activa, la anterior y, como mucho, una en uso que Windows no deja borrar
        carpetas.ShouldContain(c => c.StartsWith("datos-2024-08", StringComparison.Ordinal));
    }

    public void Dispose()
    {
        _servicio.Dispose();
        _almacen.Dispose();
        _directorio.Dispose();
    }

    private sealed class ObservadorFalso : IObservadorDeDatos
    {
        public int Avisos { get; private set; }

        public Task AlCambiarLosDatosAsync(InstantaneaDatos nueva, CancellationToken cancelacion)
        {
            Avisos++;
            return Task.CompletedTask;
        }
    }

    /// <summary>Un origen que sirve los ficheros de una carpeta local, con fallos y ficheros enormes a demanda.</summary>
    private sealed class OrigenFalso : IOrigenDeDatos
    {
        public string? Etiqueta { get; set; }

        public string? Carpeta { get; set; }

        public Exception? Fallo { get; set; }

        public bool FicheroGigante { get; set; }

        public int Descargas { get; private set; }

        public Task<PaqueteRemoto?> ObtenerUltimoAsync(CancellationToken cancelacion)
        {
            if (Fallo is not null)
            {
                throw Fallo;
            }

            if (Etiqueta is null || Carpeta is null)
            {
                return Task.FromResult<PaqueteRemoto?>(null);
            }

            var carpeta = Carpeta;
            return Task.FromResult<PaqueteRemoto?>(new PaqueteRemoto(Etiqueta, (fichero, _) =>
            {
                if (fichero == FicherosDelPaquete.BaseDeDatos)
                {
                    Descargas++;
                    if (FicheroGigante)
                    {
                        return Task.FromResult<Stream>(new FlujoInfinito());
                    }
                }

                return Task.FromResult<Stream>(new MemoryStream(File.ReadAllBytes(Path.Combine(carpeta, fichero))));
            }));
        }
    }

    private sealed class FlujoInfinito : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => count;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => ValueTask.FromResult(buffer.Length);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

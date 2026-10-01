namespace MurciaDatos.Api.Tests;

/// <summary>Una API con datos para todos los tests de solo lectura de una clase (se crea una vez).</summary>
public sealed class ApiCompartida : IAsyncLifetime
{
    public ApiDePrueba Api { get; } = new();

    public HttpClient Cliente { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        await Api.EsperarDatosAsync();
        Cliente = Api.CreateClient();
    }

    public ValueTask DisposeAsync()
    {
        Cliente.Dispose();
        Api.Dispose();
        return ValueTask.CompletedTask;
    }
}

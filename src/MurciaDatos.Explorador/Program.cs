using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

using MurciaDatos.Explorador;
using MurciaDatos.Explorador.Servicios;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// El explorador llama a la misma API que sirve su página: mismo origen, sin CORS.
builder.Services.AddScoped<IClienteApi>(_ => new ClienteApi(new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) }));

await builder.Build().RunAsync();

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace MurciaDatos.Explorador.Componentes;

/// <summary>
/// Un elemento <c>&lt;text&gt;</c> de SVG. Razor reserva la etiqueta <c>&lt;text&gt;</c> para otra cosa y no deja usarla con
/// atributos, así que se pinta desde C#.
/// </summary>
public sealed class TextoSvg : ComponentBase
{
    [Parameter] public string X { get; set; } = "0";

    [Parameter] public string Y { get; set; } = "0";

    [Parameter] public string? Ancla { get; set; }

    [Parameter] public string? LineaBase { get; set; }

    [Parameter] public string? Clase { get; set; }

    [Parameter] public RenderFragment? ChildContent { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "text");
        builder.AddAttribute(1, "x", X);
        builder.AddAttribute(2, "y", Y);
        builder.AddAttribute(3, "class", Clase);
        builder.AddAttribute(4, "text-anchor", Ancla);
        builder.AddAttribute(5, "dominant-baseline", LineaBase);
        builder.AddContent(6, ChildContent);
        builder.CloseElement();
    }
}

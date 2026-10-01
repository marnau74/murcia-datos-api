// Lo único que el explorador no puede hacer desde C#: copiar al portapapeles y mostrar el aviso de error de Blazor.
window.explorador = {
    copiar: async function (texto) {
        if (navigator.clipboard && window.isSecureContext) {
            await navigator.clipboard.writeText(texto);
            return;
        }

        // Alternativa para contextos no seguros (http sin localhost).
        const area = document.createElement("textarea");
        area.value = texto;
        area.setAttribute("readonly", "");
        area.className = "fuera-de-pantalla";
        document.body.appendChild(area);
        area.select();
        document.execCommand("copy");
        document.body.removeChild(area);
    },
};

// Blazor intenta mostrar #blazor-error-ui quitándole el atributo "hidden" (sin estilos en línea).
window.addEventListener("error", function () {
    const aviso = document.getElementById("blazor-error-ui");
    if (aviso) {
        aviso.hidden = false;
    }
});

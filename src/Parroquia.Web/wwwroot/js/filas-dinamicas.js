// Colecciones dinámicas de los formularios (padrinos, testigos…): alta con htmx, baja sin recargar.
// Marcado esperado: un contenedor con data-filas; cada fila con data-fila y un botón con data-quitar-fila.
(function () {
    document.addEventListener("click", function (e) {
        var boton = e.target.closest("[data-quitar-fila]");
        if (!boton) return;
        var fila = boton.closest("[data-fila]");
        var contenedor = fila && fila.closest("[data-filas]");
        if (!fila || !contenedor) return;

        // El foco pasa a la fila anterior (o al botón de agregar) para no perder la navegación por teclado.
        var siguiente = fila.previousElementSibling || fila.nextElementSibling;
        fila.remove();
        var destino = siguiente ? siguiente.querySelector("input") : document.querySelector("[data-agregar-fila]");
        if (destino) destino.focus();
    });

    // Tras agregar una fila con htmx, el foco va a su campo.
    document.body.addEventListener("htmx:afterSwap", function (e) {
        var contenedor = e.detail.target;
        if (!contenedor || !contenedor.hasAttribute || !contenedor.hasAttribute("data-filas")) return;
        var campos = contenedor.querySelectorAll("[data-fila] input");
        if (campos.length) campos[campos.length - 1].focus();
    });
})();

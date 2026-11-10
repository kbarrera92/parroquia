// Formularios con data-confirmar="mensaje" piden confirmación antes de enviarse (sin JavaScript en línea: compatible con CSP).
document.addEventListener("submit", function (e) {
    var mensaje = e.target && e.target.getAttribute && e.target.getAttribute("data-confirmar");
    if (mensaje && !window.confirm(mensaje)) e.preventDefault();
});

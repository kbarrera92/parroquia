using Microsoft.AspNetCore.Identity;

namespace Parroquia.Infraestructura.Identidad;

public class Usuario : IdentityUser
{
    public string NombreCompleto { get; set; } = "";

    /// <summary>Usuario desactivado no puede iniciar sesión (no se borra: queda en la bitácora).</summary>
    public bool Activo { get; set; } = true;

    /// <summary>Contraseña temporal (alta o restablecimiento): el usuario debe cambiarla antes de usar el sistema.</summary>
    public bool DebeCambiarPassword { get; set; }

    public DateTimeOffset CreadoEn { get; set; }

    /// <summary>
    /// Último día (hora de Guatemala) en que la cuenta puede entrar; null = sin vencimiento. Para capturistas temporales.
    /// Pasada esa fecha no inicia sesión y sus sesiones abiertas se cierran (no se desactiva: el Administrador la extiende).
    /// </summary>
    public DateOnly? VigenteHasta { get; set; }

    /// <summary>La fecha de vencimiento ya pasó (<paramref name="hoy"/> = hoy en Guatemala).</summary>
    public bool Vencida(DateOnly hoy) => VigenteHasta is { } hasta && hasta < hoy;

    /// <summary>Activa y no vencida.</summary>
    public bool PuedeIngresar(DateOnly hoy) => Activo && !Vencida(hoy);
}

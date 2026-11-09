using Microsoft.AspNetCore.Identity;

namespace Parroquia.Infraestructura.Identidad;

/// <summary>Mensajes de Identity en español (por defecto salen en inglés).</summary>
public sealed class DescriptorErroresIdentidad : IdentityErrorDescriber
{
    public override IdentityError DefaultError() => new() { Code = nameof(DefaultError), Description = "Ocurrió un error inesperado." };
    public override IdentityError PasswordMismatch() => new() { Code = nameof(PasswordMismatch), Description = "La contraseña actual no es correcta." };
    public override IdentityError PasswordTooShort(int length) => new() { Code = nameof(PasswordTooShort), Description = $"La contraseña debe tener al menos {length} caracteres." };
    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars) => new() { Code = nameof(PasswordRequiresUniqueChars), Description = $"La contraseña debe tener al menos {uniqueChars} caracteres distintos." };
    public override IdentityError InvalidUserName(string? userName) => new() { Code = nameof(InvalidUserName), Description = "El nombre de usuario no es válido." };
    public override IdentityError DuplicateUserName(string userName) => new() { Code = nameof(DuplicateUserName), Description = $"El usuario «{userName}» ya existe." };
    public override IdentityError InvalidEmail(string? email) => new() { Code = nameof(InvalidEmail), Description = "El correo no es válido." };
    public override IdentityError DuplicateEmail(string email) => new() { Code = nameof(DuplicateEmail), Description = "Ese correo ya está en uso." };
    public override IdentityError DuplicateRoleName(string role) => new() { Code = nameof(DuplicateRoleName), Description = $"El rol «{role}» ya existe." };
    public override IdentityError InvalidRoleName(string? role) => new() { Code = nameof(InvalidRoleName), Description = "El nombre de rol no es válido." };
    public override IdentityError ConcurrencyFailure() => new() { Code = nameof(ConcurrencyFailure), Description = "Otro usuario modificó este registro. Recargue e intente de nuevo." };
    public override IdentityError UserAlreadyInRole(string role) => new() { Code = nameof(UserAlreadyInRole), Description = $"El usuario ya tiene el rol «{role}»." };
    public override IdentityError UserNotInRole(string role) => new() { Code = nameof(UserNotInRole), Description = $"El usuario no tiene el rol «{role}»." };
    public override IdentityError UserLockoutNotEnabled() => new() { Code = nameof(UserLockoutNotEnabled), Description = "El bloqueo no está habilitado para este usuario." };
}

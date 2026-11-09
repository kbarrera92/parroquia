using System.Security.Cryptography;

namespace Parroquia.Infraestructura.Identidad;

/// <summary>Contraseña temporal aleatoria (CSPRNG) sin caracteres ambiguos (0/O, 1/l/I) para entregarla en persona.</summary>
public static class GeneradorContrasenaTemporal
{
    public const int Longitud = 16;

    private const string Alfabeto = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";

    public static string Generar() =>
        string.Create(Longitud, 0, static (destino, _) =>
        {
            for (var i = 0; i < destino.Length; i++)
                destino[i] = Alfabeto[RandomNumberGenerator.GetInt32(Alfabeto.Length)];
        });
}

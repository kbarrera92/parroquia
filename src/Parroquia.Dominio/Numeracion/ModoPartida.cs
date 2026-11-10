namespace Parroquia.Dominio.Numeracion;

/// <summary>Cuándo vuelve a 1 la partida en un libro digital. El orden fija el valor por defecto (0 = correlativa).</summary>
public enum ModoPartida
{
    /// <summary>Nunca reinicia: sigue subiendo de un libro al siguiente (1, 2, 3… en todo el sistema).</summary>
    Correlativa = 0,

    /// <summary>Correlativa dentro del libro; vuelve a 1 al abrir un libro nuevo.</summary>
    ReiniciaPorLibro = 1,

    /// <summary>Vuelve a 1 en cada folio (y por lo tanto en cada libro).</summary>
    ReiniciaPorFolio = 2,
}

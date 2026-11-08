using Parroquia.Dominio.Comun;

namespace Parroquia.Dominio.Catalogos;

/// <summary>Elemento de un catálogo administrable: se activa/desactiva, nunca se borra.</summary>
public interface ICatalogoItem
{
    string Valor { get; set; }
    bool Activo { get; set; }
}

/// <summary>Sacerdote que puede figurar como ministro. El formulario también permite escribir otro nombre.</summary>
public class Ministro : EntidadAuditable, ICatalogoItem
{
    public string Nombre { get; set; } = "";
    public bool Activo { get; set; } = true;

    string ICatalogoItem.Valor { get => Nombre; set => Nombre = value; }
}

/// <summary>Tratamiento de un contrayente: "don", "la Srita.", etc.</summary>
public class Tratamiento : EntidadAuditable, ICatalogoItem
{
    public string Texto { get; set; } = "";
    public bool Activo { get; set; } = true;

    string ICatalogoItem.Valor { get => Texto; set => Texto = value; }
}

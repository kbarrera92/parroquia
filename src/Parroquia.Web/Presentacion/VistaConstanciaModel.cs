using Parroquia.Aplicacion.Constancias;

namespace Parroquia.Web.Presentacion;

/// <summary>Lo que necesita el parcial <c>_VistaConstancia</c>: el mismo documento que se maqueta en el PDF.</summary>
public sealed record VistaConstanciaModel(DocumentoConstancia Documento, string? LogoUrl);

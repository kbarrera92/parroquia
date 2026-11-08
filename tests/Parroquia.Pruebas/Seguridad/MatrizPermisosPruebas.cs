using Parroquia.Aplicacion.Seguridad;

namespace Parroquia.Pruebas.Seguridad;

public class MatrizPermisosPruebas
{
    [Fact]
    public void Capturista_crea_edita_anula_pero_no_imprime()
    {
        var p = MatrizPermisos.De(Roles.Capturista);

        p.Should().BeEquivalentTo(new[]
        {
            Permisos.RegistrosVer, Permisos.RegistrosBuscar,
            Permisos.RegistrosCrear, Permisos.RegistrosEditar, Permisos.RegistrosAnular,
        });
    }

    [Fact]
    public void Secretaria_solo_lee_e_imprime()
    {
        var p = MatrizPermisos.De(Roles.Secretaria);

        p.Should().BeEquivalentTo(new[]
        {
            Permisos.RegistrosVer, Permisos.RegistrosBuscar,
            Permisos.ConstanciasImprimir, Permisos.ConstanciasReimprimir,
        });
    }

    [Fact]
    public void Administrador_tiene_todos_los_permisos()
    {
        MatrizPermisos.De(Roles.Administrador).Should().BeEquivalentTo(Permisos.Todos);
    }

    [Fact]
    public void Solo_el_administrador_cambia_fecha_revierte_anulacion_y_ve_auditoria()
    {
        string[] exclusivos =
        [
            Permisos.ConstanciasCambiarFecha, Permisos.RegistrosRevertirAnulacion, Permisos.AuditoriaVer,
            Permisos.CatalogosGestionar, Permisos.Configuracion, Permisos.UsuariosGestionar,
        ];

        foreach (var permiso in exclusivos)
        {
            MatrizPermisos.De(Roles.Capturista).Should().NotContain(permiso);
            MatrizPermisos.De(Roles.Secretaria).Should().NotContain(permiso);
            MatrizPermisos.De(Roles.Administrador).Should().Contain(permiso);
        }
    }

    [Fact]
    public void Todos_los_roles_estan_en_la_matriz()
    {
        MatrizPermisos.PorRol.Keys.Should().BeEquivalentTo(Roles.Todos);
    }
}

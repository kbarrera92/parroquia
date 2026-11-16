using Parroquia.Aplicacion.Seguridad;

namespace Parroquia.Pruebas.Seguridad;

public class MatrizPermisosPruebas
{
    [Fact]
    public void Capturista_transcribe_edita_anula_pero_no_asienta_ni_imprime()
    {
        var p = MatrizPermisos.De(Roles.Capturista);

        p.Should().BeEquivalentTo(new[]
        {
            Permisos.RegistrosVer, Permisos.RegistrosBuscar,
            Permisos.RegistrosTranscribir, Permisos.RegistrosEditar, Permisos.RegistrosAnular,
        });
        p.Should().NotContain(Permisos.RegistrosAsentarNuevo);
    }

    [Fact]
    public void Secretaria_asienta_lo_nuevo_corrige_e_imprime_pero_no_transcribe()
    {
        var p = MatrizPermisos.De(Roles.Secretaria);

        p.Should().BeEquivalentTo(new[]
        {
            Permisos.RegistrosVer, Permisos.RegistrosBuscar,
            Permisos.RegistrosAsentarNuevo, Permisos.RegistrosEditar, Permisos.RegistrosAnular,
            Permisos.ConstanciasImprimir, Permisos.ConstanciasReimprimir,
        });
        p.Should().NotContain(Permisos.RegistrosTranscribir);
    }

    [Fact]
    public void Todo_permiso_tiene_descripcion_y_los_reservados_son_solo_del_administrador()
    {
        foreach (var permiso in Permisos.Todos)
            Permisos.Descripciones.Should().ContainKey(permiso);

        foreach (var reservado in Permisos.Reservados)
        {
            MatrizPermisos.De(Roles.Capturista).Should().NotContain(reservado);
            MatrizPermisos.De(Roles.Secretaria).Should().NotContain(reservado);
        }
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

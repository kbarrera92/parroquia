using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Parroquia.Infraestructura.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class DividirPermisoCrear : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // «Registros.Crear» se divide en Transcribir (libro físico) y AsentarNuevo (numera el sistema).
            // Solo toca la matriz de permisos ya guardada en los roles; sin cambio de modelo.
            migrationBuilder.Sql(@"
INSERT INTO ""AspNetRoleClaims"" (role_id, claim_type, claim_value)
SELECT rc.role_id, 'permiso', n.permiso
FROM ""AspNetRoleClaims"" rc
JOIN ""AspNetRoles"" r ON r.id = rc.role_id
CROSS JOIN LATERAL (VALUES ('Registros.Transcribir'), ('Registros.AsentarNuevo')) AS n(permiso)
WHERE rc.claim_type = 'permiso' AND rc.claim_value = 'Registros.Crear'
  AND (r.name <> 'Capturista' OR n.permiso = 'Registros.Transcribir')
  AND (r.name <> 'Secretaria' OR n.permiso = 'Registros.AsentarNuevo')
  AND NOT EXISTS (SELECT 1 FROM ""AspNetRoleClaims"" x
                  WHERE x.role_id = rc.role_id AND x.claim_type = 'permiso' AND x.claim_value = n.permiso);

-- La Secretaria también corrige y anula (corregir un asiento del sistema = anular y asentar de nuevo).
INSERT INTO ""AspNetRoleClaims"" (role_id, claim_type, claim_value)
SELECT r.id, 'permiso', n.permiso
FROM ""AspNetRoles"" r
CROSS JOIN (VALUES ('Registros.AsentarNuevo'), ('Registros.Editar'), ('Registros.Anular')) AS n(permiso)
WHERE r.name = 'Secretaria'
  AND NOT EXISTS (SELECT 1 FROM ""AspNetRoleClaims"" x
                  WHERE x.role_id = r.id AND x.claim_type = 'permiso' AND x.claim_value = n.permiso);

DELETE FROM ""AspNetRoleClaims"" WHERE claim_type = 'permiso' AND claim_value = 'Registros.Crear';
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}

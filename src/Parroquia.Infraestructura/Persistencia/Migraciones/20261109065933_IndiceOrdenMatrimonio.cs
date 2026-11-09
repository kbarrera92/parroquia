using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Parroquia.Infraestructura.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class IndiceOrdenMatrimonio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Índice de orden del listado de Matrimonio. SQL a mano: EF no admite HasIndex sobre un complex type.
            migrationBuilder.Sql(
                "CREATE INDEX ix_matrimonio_esposo_apellidos_esposo_nombres ON matrimonio (esposo_apellidos, esposo_nombres);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_matrimonio_esposo_apellidos_esposo_nombres;");
        }
    }
}

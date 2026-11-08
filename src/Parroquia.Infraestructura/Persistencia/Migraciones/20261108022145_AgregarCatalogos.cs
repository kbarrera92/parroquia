using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Parroquia.Infraestructura.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class AgregarCatalogos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "secuencia_id",
                incrementBy: 10);

            migrationBuilder.CreateTable(
                name: "ministros",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    nombre = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    creado_por = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    modificado_por = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modificado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ministros", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tratamientos",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    texto = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    creado_por = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    modificado_por = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modificado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tratamientos", x => x.id);
                });

            // Respaldo del servicio (que además ignora tildes): no puede haber dos nombres iguales.
            migrationBuilder.Sql("CREATE UNIQUE INDEX ux_ministros_nombre ON ministros (lower(btrim(nombre)));");
            migrationBuilder.Sql("CREATE UNIQUE INDEX ux_tratamientos_texto ON tratamientos (lower(btrim(texto)));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ministros");

            migrationBuilder.DropTable(
                name: "tratamientos");

            migrationBuilder.DropSequence(
                name: "secuencia_id");
        }
    }
}

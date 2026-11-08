using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Parroquia.Infraestructura.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class AgregarBitacoraAcceso : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bitacora_accesos",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    fecha = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    usuario_id = table.Column<string>(type: "text", nullable: true),
                    usuario_nombre = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    evento = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    entidad = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    entidad_id = table.Column<long>(type: "bigint", nullable: true),
                    detalle = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ip = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bitacora_accesos", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bitacora_accesos_fecha",
                table: "bitacora_accesos",
                column: "fecha");

            migrationBuilder.CreateIndex(
                name: "ix_bitacora_accesos_usuario_nombre_fecha",
                table: "bitacora_accesos",
                columns: new[] { "usuario_nombre", "fecha" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bitacora_accesos");
        }
    }
}

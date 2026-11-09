using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Parroquia.Infraestructura.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class AgregarVinculo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "vinculo",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    tipo_origen = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    id_origen = table.Column<long>(type: "bigint", nullable: false),
                    persona_origen = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    tipo_destino = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    id_destino = table.Column<long>(type: "bigint", nullable: false),
                    persona_destino = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    anulado = table.Column<bool>(type: "boolean", nullable: false),
                    motivo_anulacion = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    creado_por = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    modificado_por = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modificado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vinculo", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_vinculo_tipo_destino_id_destino",
                table: "vinculo",
                columns: new[] { "tipo_destino", "id_destino" });

            migrationBuilder.CreateIndex(
                name: "ix_vinculo_tipo_origen_id_origen",
                table: "vinculo",
                columns: new[] { "tipo_origen", "id_origen" });

            migrationBuilder.CreateIndex(
                name: "ix_vinculo_tipo_origen_id_origen_persona_origen_tipo_destino_i",
                table: "vinculo",
                columns: new[] { "tipo_origen", "id_origen", "persona_origen", "tipo_destino", "id_destino", "persona_destino" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "vinculo");
        }
    }
}

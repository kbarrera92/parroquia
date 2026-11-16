using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Parroquia.Infraestructura.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class AgregarSupletoria : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "supletoria",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    tipo_sacramento = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    registro_id = table.Column<long>(type: "bigint", nullable: false),
                    libro = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    folio = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    partida = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    libro_digital_id = table.Column<long>(type: "bigint", nullable: true),
                    libro_orden = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    folio_orden = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    partida_orden = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    testigo = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    parentesco_testigo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    anulado = table.Column<bool>(type: "boolean", nullable: false),
                    motivo_anulacion = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    fecha_supletoria_clave_orden = table.Column<int>(type: "integer", nullable: false, computedColumnSql: "COALESCE(fecha_supletoria_anio, 0) * 10000 + COALESCE(fecha_supletoria_mes, 0) * 100 + COALESCE(fecha_supletoria_dia, 0)", stored: true),
                    fecha_supletoria_anio = table.Column<short>(type: "smallint", nullable: true),
                    fecha_supletoria_dia = table.Column<short>(type: "smallint", nullable: true),
                    fecha_supletoria_mes = table.Column<short>(type: "smallint", nullable: true),
                    creado_por = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    modificado_por = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modificado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_supletoria", x => x.id);
                    table.ForeignKey(
                        name: "fk_supletoria_libro_digital_libro_digital_id",
                        column: x => x.libro_digital_id,
                        principalTable: "libro_digital",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_supletoria_anulado",
                table: "supletoria",
                column: "anulado");

            migrationBuilder.CreateIndex(
                name: "ix_supletoria_fecha_supletoria_clave_orden",
                table: "supletoria",
                column: "fecha_supletoria_clave_orden");

            migrationBuilder.CreateIndex(
                name: "ix_supletoria_libro_digital_id",
                table: "supletoria",
                column: "libro_digital_id");

            migrationBuilder.CreateIndex(
                name: "ix_supletoria_libro_orden_folio_orden_partida_orden",
                table: "supletoria",
                columns: new[] { "libro_orden", "folio_orden", "partida_orden" });

            migrationBuilder.CreateIndex(
                name: "ix_supletoria_tipo_sacramento_registro_id",
                table: "supletoria",
                columns: new[] { "tipo_sacramento", "registro_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "supletoria");
        }
    }
}

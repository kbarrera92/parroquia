using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Parroquia.Infraestructura.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class AgregarConfirmacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "confirmacion",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    nombres = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    apellidos = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    padrinos = table.Column<List<string>>(type: "text[]", nullable: false),
                    fecha_confirmacion_clave_orden = table.Column<int>(type: "integer", nullable: false, computedColumnSql: "COALESCE(fecha_confirmacion_anio, 0) * 10000 + COALESCE(fecha_confirmacion_mes, 0) * 100 + COALESCE(fecha_confirmacion_dia, 0)", stored: true),
                    fecha_confirmacion_anio = table.Column<short>(type: "smallint", nullable: true),
                    fecha_confirmacion_dia = table.Column<short>(type: "smallint", nullable: true),
                    fecha_confirmacion_mes = table.Column<short>(type: "smallint", nullable: true),
                    creado_por = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    modificado_por = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modificado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    libro = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    folio = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    partida = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    libro_orden = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    folio_orden = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    partida_orden = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    busqueda_normalizada = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    revisar = table.Column<bool>(type: "boolean", nullable: false),
                    motivo_revision = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    anulado = table.Column<bool>(type: "boolean", nullable: false),
                    motivo_anulacion = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    notas_marginales = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ministro_id = table.Column<long>(type: "bigint", nullable: true),
                    ministro_texto = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_confirmacion", x => x.id);
                    table.ForeignKey(
                        name: "fk_confirmacion_ministros_ministro_id",
                        column: x => x.ministro_id,
                        principalTable: "ministros",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_confirmacion_anulado",
                table: "confirmacion",
                column: "anulado");

            migrationBuilder.CreateIndex(
                name: "ix_confirmacion_apellidos_nombres",
                table: "confirmacion",
                columns: new[] { "apellidos", "nombres" });

            migrationBuilder.CreateIndex(
                name: "ix_confirmacion_busqueda_normalizada",
                table: "confirmacion",
                column: "busqueda_normalizada")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_confirmacion_fecha_confirmacion_clave_orden",
                table: "confirmacion",
                column: "fecha_confirmacion_clave_orden");

            migrationBuilder.CreateIndex(
                name: "ix_confirmacion_libro_orden_folio_orden_partida_orden",
                table: "confirmacion",
                columns: new[] { "libro_orden", "folio_orden", "partida_orden" });

            migrationBuilder.CreateIndex(
                name: "ix_confirmacion_ministro_id",
                table: "confirmacion",
                column: "ministro_id");

            migrationBuilder.CreateIndex(
                name: "ix_confirmacion_revisar",
                table: "confirmacion",
                column: "revisar");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "confirmacion");
        }
    }
}

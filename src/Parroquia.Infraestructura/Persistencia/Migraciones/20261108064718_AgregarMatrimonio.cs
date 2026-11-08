using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Parroquia.Infraestructura.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class AgregarMatrimonio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "matrimonio",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    lugar_celebracion = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    testigos = table.Column<List<string>>(type: "text[]", nullable: false),
                    busqueda_esposo = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    busqueda_esposa = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    fecha_matrimonio_clave_orden = table.Column<int>(type: "integer", nullable: false, computedColumnSql: "COALESCE(fecha_matrimonio_anio, 0) * 10000 + COALESCE(fecha_matrimonio_mes, 0) * 100 + COALESCE(fecha_matrimonio_dia, 0)", stored: true),
                    esposa_apellidos = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    esposa_edad = table.Column<short>(type: "smallint", nullable: true),
                    esposa_feligres_de = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    esposa_madre = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    esposa_nombres = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    esposa_originario_de = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    esposa_padre = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    esposa_tratamiento = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    esposo_apellidos = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    esposo_edad = table.Column<short>(type: "smallint", nullable: true),
                    esposo_feligres_de = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    esposo_madre = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    esposo_nombres = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    esposo_originario_de = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    esposo_padre = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    esposo_tratamiento = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    fecha_matrimonio_anio = table.Column<short>(type: "smallint", nullable: true),
                    fecha_matrimonio_dia = table.Column<short>(type: "smallint", nullable: true),
                    fecha_matrimonio_mes = table.Column<short>(type: "smallint", nullable: true),
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
                    busqueda_normalizada = table.Column<string>(type: "character varying(800)", maxLength: 800, nullable: false),
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
                    table.PrimaryKey("pk_matrimonio", x => x.id);
                    table.ForeignKey(
                        name: "fk_matrimonio_ministros_ministro_id",
                        column: x => x.ministro_id,
                        principalTable: "ministros",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_matrimonio_anulado",
                table: "matrimonio",
                column: "anulado");

            migrationBuilder.CreateIndex(
                name: "ix_matrimonio_busqueda_esposa",
                table: "matrimonio",
                column: "busqueda_esposa")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_matrimonio_busqueda_esposo",
                table: "matrimonio",
                column: "busqueda_esposo")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_matrimonio_busqueda_normalizada",
                table: "matrimonio",
                column: "busqueda_normalizada")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_matrimonio_fecha_matrimonio_clave_orden",
                table: "matrimonio",
                column: "fecha_matrimonio_clave_orden");

            migrationBuilder.CreateIndex(
                name: "ix_matrimonio_libro_orden_folio_orden_partida_orden",
                table: "matrimonio",
                columns: new[] { "libro_orden", "folio_orden", "partida_orden" });

            migrationBuilder.CreateIndex(
                name: "ix_matrimonio_ministro_id",
                table: "matrimonio",
                column: "ministro_id");

            migrationBuilder.CreateIndex(
                name: "ix_matrimonio_revisar",
                table: "matrimonio",
                column: "revisar");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "matrimonio");
        }
    }
}

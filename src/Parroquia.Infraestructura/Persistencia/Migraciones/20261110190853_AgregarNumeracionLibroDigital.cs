using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Parroquia.Infraestructura.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class AgregarNumeracionLibroDigital : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "libro_digital_id",
                table: "matrimonio",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "libro_digital_id",
                table: "confirmacion",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "libro_digital_id",
                table: "comunion",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "libro_digital_id",
                table: "bautismo",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "libro_digital",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    tipo_sacramento = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    numero = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    folio_inicial = table.Column<int>(type: "integer", nullable: false),
                    partida_inicial = table.Column<int>(type: "integer", nullable: false),
                    partidas_por_folio = table.Column<int>(type: "integer", nullable: false),
                    folios_por_libro = table.Column<int>(type: "integer", nullable: false),
                    partida_reinicia_por_folio = table.Column<bool>(type: "boolean", nullable: false),
                    folio_actual = table.Column<int>(type: "integer", nullable: false),
                    partidas_en_folio = table.Column<int>(type: "integer", nullable: false),
                    ultima_partida = table.Column<int>(type: "integer", nullable: false),
                    entregados = table.Column<int>(type: "integer", nullable: false),
                    abierto = table.Column<bool>(type: "boolean", nullable: false),
                    abierto_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    cerrado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    creado_por = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    modificado_por = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    modificado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_libro_digital", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_matrimonio_libro_digital_id",
                table: "matrimonio",
                column: "libro_digital_id");

            migrationBuilder.CreateIndex(
                name: "ix_confirmacion_libro_digital_id",
                table: "confirmacion",
                column: "libro_digital_id");

            migrationBuilder.CreateIndex(
                name: "ix_comunion_libro_digital_id",
                table: "comunion",
                column: "libro_digital_id");

            migrationBuilder.CreateIndex(
                name: "ix_bautismo_libro_digital_id",
                table: "bautismo",
                column: "libro_digital_id");

            migrationBuilder.CreateIndex(
                name: "ix_libro_digital_tipo_sacramento",
                table: "libro_digital",
                column: "tipo_sacramento",
                unique: true,
                filter: "abierto");

            migrationBuilder.CreateIndex(
                name: "ix_libro_digital_tipo_sacramento_numero",
                table: "libro_digital",
                columns: new[] { "tipo_sacramento", "numero" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_bautismo_libro_digital_libro_digital_id",
                table: "bautismo",
                column: "libro_digital_id",
                principalTable: "libro_digital",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_comunion_libro_digital_libro_digital_id",
                table: "comunion",
                column: "libro_digital_id",
                principalTable: "libro_digital",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_confirmacion_libro_digital_libro_digital_id",
                table: "confirmacion",
                column: "libro_digital_id",
                principalTable: "libro_digital",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_matrimonio_libro_digital_libro_digital_id",
                table: "matrimonio",
                column: "libro_digital_id",
                principalTable: "libro_digital",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_bautismo_libro_digital_libro_digital_id",
                table: "bautismo");

            migrationBuilder.DropForeignKey(
                name: "fk_comunion_libro_digital_libro_digital_id",
                table: "comunion");

            migrationBuilder.DropForeignKey(
                name: "fk_confirmacion_libro_digital_libro_digital_id",
                table: "confirmacion");

            migrationBuilder.DropForeignKey(
                name: "fk_matrimonio_libro_digital_libro_digital_id",
                table: "matrimonio");

            migrationBuilder.DropTable(
                name: "libro_digital");

            migrationBuilder.DropIndex(
                name: "ix_matrimonio_libro_digital_id",
                table: "matrimonio");

            migrationBuilder.DropIndex(
                name: "ix_confirmacion_libro_digital_id",
                table: "confirmacion");

            migrationBuilder.DropIndex(
                name: "ix_comunion_libro_digital_id",
                table: "comunion");

            migrationBuilder.DropIndex(
                name: "ix_bautismo_libro_digital_id",
                table: "bautismo");

            migrationBuilder.DropColumn(
                name: "libro_digital_id",
                table: "matrimonio");

            migrationBuilder.DropColumn(
                name: "libro_digital_id",
                table: "confirmacion");

            migrationBuilder.DropColumn(
                name: "libro_digital_id",
                table: "comunion");

            migrationBuilder.DropColumn(
                name: "libro_digital_id",
                table: "bautismo");
        }
    }
}

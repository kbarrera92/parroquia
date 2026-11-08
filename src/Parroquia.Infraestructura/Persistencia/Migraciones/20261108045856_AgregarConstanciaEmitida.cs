using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Parroquia.Infraestructura.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class AgregarConstanciaEmitida : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "constancias_emitidas",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    tipo_sacramento = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    registro_id = table.Column<long>(type: "bigint", nullable: false),
                    emitida_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    emitida_por = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    usuario_id = table.Column<string>(type: "text", nullable: true),
                    fecha_impresa = table.Column<DateOnly>(type: "date", nullable: false),
                    fecha_ajustada_por = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    firmante = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    instantanea_json = table.Column<string>(type: "jsonb", nullable: false),
                    hash_pdf = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    con_advertencias = table.Column<bool>(type: "boolean", nullable: false),
                    solicitud_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_constancias_emitidas", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_constancias_emitidas_emitida_en",
                table: "constancias_emitidas",
                column: "emitida_en");

            migrationBuilder.CreateIndex(
                name: "ix_constancias_emitidas_tipo_sacramento_registro_id",
                table: "constancias_emitidas",
                columns: new[] { "tipo_sacramento", "registro_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "constancias_emitidas");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Parroquia.Infraestructura.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class ModoPartidaLibroDigital : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "modo_partida",
                table: "libro_digital",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Correlativa");

            // Conserva lo ya configurado: el antiguo "reinicia por folio" pasa al modo equivalente.
            migrationBuilder.Sql("UPDATE libro_digital SET modo_partida = 'ReiniciaPorFolio' WHERE partida_reinicia_por_folio;");

            migrationBuilder.DropColumn(
                name: "partida_reinicia_por_folio",
                table: "libro_digital");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "partida_reinicia_por_folio",
                table: "libro_digital",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql("UPDATE libro_digital SET partida_reinicia_por_folio = (modo_partida = 'ReiniciaPorFolio');");

            migrationBuilder.DropColumn(
                name: "modo_partida",
                table: "libro_digital");
        }
    }
}

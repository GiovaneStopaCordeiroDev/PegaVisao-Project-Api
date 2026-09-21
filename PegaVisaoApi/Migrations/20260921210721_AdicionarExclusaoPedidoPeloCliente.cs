using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PegaVisaoApi.Migrations
{
    /// <inheritdoc />
    public partial class AdicionarExclusaoPedidoPeloCliente : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ExcluidoPeloCliente",
                table: "Pedidos",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExcluidoPeloCliente",
                table: "Pedidos");
        }
    }
}

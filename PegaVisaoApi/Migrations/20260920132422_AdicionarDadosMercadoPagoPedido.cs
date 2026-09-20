using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PegaVisaoApi.Migrations
{
    /// <inheritdoc />
    public partial class AdicionarDadosMercadoPagoPedido : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MercadoPagoCheckoutUrl",
                table: "Pedidos",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MercadoPagoOrderId",
                table: "Pedidos",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MercadoPagoStatus",
                table: "Pedidos",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MercadoPagoCheckoutUrl",
                table: "Pedidos");

            migrationBuilder.DropColumn(
                name: "MercadoPagoOrderId",
                table: "Pedidos");

            migrationBuilder.DropColumn(
                name: "MercadoPagoStatus",
                table: "Pedidos");
        }
    }
}

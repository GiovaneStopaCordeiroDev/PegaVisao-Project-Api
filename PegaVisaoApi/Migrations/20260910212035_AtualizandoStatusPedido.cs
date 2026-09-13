using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PegaVisaoApi.Migrations
{
    /// <inheritdoc />
    public partial class AtualizandoStatusPedido : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ItemPedidos_VariacaoProdutos_VariacaoProdutoId",
                table: "ItemPedidos");

            migrationBuilder.Sql("""
                ALTER TABLE "Pedidos"
                ALTER COLUMN "Status" TYPE integer
                USING CASE "Status"
                    WHEN 'Pendente' THEN 0
                    WHEN 'Enviado' THEN 1
                    WHEN 'Entregue' THEN 2
                    WHEN 'Cancelado' THEN 3
                    ELSE 0
                END;
            """);

            migrationBuilder.AddForeignKey(
                name: "FK_ItemPedidos_VariacaoProdutos_VariacaoProdutoId",
                table: "ItemPedidos",
                column: "VariacaoProdutoId",
                principalTable: "VariacaoProdutos",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ItemPedidos_VariacaoProdutos_VariacaoProdutoId",
                table: "ItemPedidos");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "Pedidos",
                type: "text",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddForeignKey(
                name: "FK_ItemPedidos_VariacaoProdutos_VariacaoProdutoId",
                table: "ItemPedidos",
                column: "VariacaoProdutoId",
                principalTable: "VariacaoProdutos",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}

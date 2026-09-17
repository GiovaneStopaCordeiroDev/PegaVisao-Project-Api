using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PegaVisaoApi.Migrations
{
    /// <inheritdoc />
    public partial class AlterarExclusaoVariacaoProduto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ItemPedidos_VariacaoProdutos_VariacaoProdutoId",
                table: "ItemPedidos");

            migrationBuilder.AddForeignKey(
                name: "FK_ItemPedidos_VariacaoProdutos_VariacaoProdutoId",
                table: "ItemPedidos",
                column: "VariacaoProdutoId",
                principalTable: "VariacaoProdutos",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ItemPedidos_VariacaoProdutos_VariacaoProdutoId",
                table: "ItemPedidos");

            migrationBuilder.AddForeignKey(
                name: "FK_ItemPedidos_VariacaoProdutos_VariacaoProdutoId",
                table: "ItemPedidos",
                column: "VariacaoProdutoId",
                principalTable: "VariacaoProdutos",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}

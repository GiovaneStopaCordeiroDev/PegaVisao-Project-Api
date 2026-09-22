using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PegaVisaoApi.Migrations
{
    /// <inheritdoc />
    public partial class AdicionarReservaEstoque : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ItemPedidos_VariacaoProdutos_VariacaoProdutoId",
                table: "ItemPedidos");

            migrationBuilder.AddColumn<int>(
                name: "EstoqueReservado",
                table: "VariacaoProdutos",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "EstadoEstoque",
                table: "Pedidos",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "ProximaConsultaEstoqueEm",
                table: "Pedidos",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Variacao_Estoque",
                table: "VariacaoProdutos",
                sql: "\"Estoque\" >= 0 AND \"EstoqueReservado\" >= 0 AND \"EstoqueReservado\" <= \"Estoque\"");

            migrationBuilder.CreateIndex(
                name: "IX_Pedidos_EstadoEstoque_ProximaConsultaEstoqueEm",
                table: "Pedidos",
                columns: new[] { "EstadoEstoque", "ProximaConsultaEstoqueEm" });

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

            migrationBuilder.DropCheckConstraint(
                name: "CK_Variacao_Estoque",
                table: "VariacaoProdutos");

            migrationBuilder.DropIndex(
                name: "IX_Pedidos_EstadoEstoque_ProximaConsultaEstoqueEm",
                table: "Pedidos");

            migrationBuilder.DropColumn(
                name: "EstoqueReservado",
                table: "VariacaoProdutos");

            migrationBuilder.DropColumn(
                name: "EstadoEstoque",
                table: "Pedidos");

            migrationBuilder.DropColumn(
                name: "ProximaConsultaEstoqueEm",
                table: "Pedidos");

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

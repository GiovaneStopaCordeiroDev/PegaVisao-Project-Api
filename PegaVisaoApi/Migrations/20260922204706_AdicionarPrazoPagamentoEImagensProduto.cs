using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PegaVisaoApi.Migrations
{
    /// <inheritdoc />
    public partial class AdicionarPrazoPagamentoEImagensProduto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImagemSecundaria",
                table: "Produtos",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImagemTerciaria",
                table: "Produtos",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PagamentoExpiraEm",
                table: "Pedidos",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImagemSecundaria",
                table: "Produtos");

            migrationBuilder.DropColumn(
                name: "ImagemTerciaria",
                table: "Produtos");

            migrationBuilder.DropColumn(
                name: "PagamentoExpiraEm",
                table: "Pedidos");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PegaVisaoApi.Migrations
{
    /// <inheritdoc />
    public partial class AdicionarFreteAoCheckout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AlturaCm",
                table: "Produtos",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ComprimentoCm",
                table: "Produtos",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LarguraCm",
                table: "Produtos",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PesoKg",
                table: "Produtos",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FreteCotacaoId",
                table: "Pedidos",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FretePrazoDias",
                table: "Pedidos",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "FreteSandbox",
                table: "Pedidos",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FreteServico",
                table: "Pedidos",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FreteServicoId",
                table: "Pedidos",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FreteTransportadora",
                table: "Pedidos",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FreteVolumesJson",
                table: "Pedidos",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ValorFrete",
                table: "Pedidos",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "CotacoesFrete",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<int>(type: "integer", nullable: false),
                    CepDestino = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    CarrinhoHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ConexaoId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Sandbox = table.Column<bool>(type: "boolean", nullable: false),
                    Subtotal = table.Column<decimal>(type: "numeric", nullable: false),
                    OpcoesJson = table.Column<string>(type: "text", nullable: false),
                    CriadaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiraEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConsumidaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CotacoesFrete", x => x.Id);
                });

            migrationBuilder.Sql("ALTER TABLE public.\"CotacoesFrete\" ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.CreateIndex(
                name: "IX_Pedidos_FreteCotacaoId",
                table: "Pedidos",
                column: "FreteCotacaoId",
                unique: true);


            migrationBuilder.CreateIndex(
                name: "IX_CotacoesFrete_ExpiraEm",
                table: "CotacoesFrete",
                column: "ExpiraEm");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CotacoesFrete");

            migrationBuilder.DropIndex(
                name: "IX_Pedidos_FreteCotacaoId",
                table: "Pedidos");

            migrationBuilder.DropColumn(
                name: "AlturaCm",
                table: "Produtos");

            migrationBuilder.DropColumn(
                name: "ComprimentoCm",
                table: "Produtos");

            migrationBuilder.DropColumn(
                name: "LarguraCm",
                table: "Produtos");

            migrationBuilder.DropColumn(
                name: "PesoKg",
                table: "Produtos");

            migrationBuilder.DropColumn(
                name: "FreteCotacaoId",
                table: "Pedidos");

            migrationBuilder.DropColumn(
                name: "FretePrazoDias",
                table: "Pedidos");

            migrationBuilder.DropColumn(
                name: "FreteSandbox",
                table: "Pedidos");

            migrationBuilder.DropColumn(
                name: "FreteServico",
                table: "Pedidos");

            migrationBuilder.DropColumn(
                name: "FreteServicoId",
                table: "Pedidos");

            migrationBuilder.DropColumn(
                name: "FreteTransportadora",
                table: "Pedidos");

            migrationBuilder.DropColumn(
                name: "FreteVolumesJson",
                table: "Pedidos");

            migrationBuilder.DropColumn(
                name: "ValorFrete",
                table: "Pedidos");
        }
    }
}

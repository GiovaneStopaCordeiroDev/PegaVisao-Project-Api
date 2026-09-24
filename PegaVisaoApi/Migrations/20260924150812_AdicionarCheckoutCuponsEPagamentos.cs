using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PegaVisaoApi.Migrations
{
    /// <inheritdoc />
    public partial class AdicionarCheckoutCuponsEPagamentos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CheckoutHash",
                table: "Pedidos",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CheckoutSessionId",
                table: "Pedidos",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CupomCodigo",
                table: "Pedidos",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CupomId",
                table: "Pedidos",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PagamentoLeaseAte",
                table: "Pedidos",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PagamentoPayloadJson",
                table: "Pedidos",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentIdempotencyKey",
                table: "Pedidos",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ValorDesconto",
                table: "Pedidos",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "Cupons",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Codigo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Descricao = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Valor = table.Column<decimal>(type: "numeric", nullable: false),
                    ValorMinimo = table.Column<decimal>(type: "numeric", nullable: false),
                    DescontoMaximo = table.Column<decimal>(type: "numeric", nullable: true),
                    InicioEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ValidadeEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Ativo = table.Column<bool>(type: "boolean", nullable: false),
                    Excluido = table.Column<bool>(type: "boolean", nullable: false),
                    LimiteTotal = table.Column<int>(type: "integer", nullable: true),
                    LimitePorUsuario = table.Column<int>(type: "integer", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cupons", x => x.Id);
                    table.CheckConstraint("CK_Cupom_Valores", "\"Valor\" > 0 AND \"ValorMinimo\" >= 0 AND (\"Tipo\" = 'Fixo' OR (\"Tipo\" = 'Percentual' AND \"Valor\" <= 100))");
                });

            migrationBuilder.CreateTable(
                name: "TentativasPagamento",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PedidoId = table.Column<int>(type: "integer", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    HttpStatus = table.Column<int>(type: "integer", nullable: true),
                    CodigoErro = table.Column<string>(type: "text", nullable: true),
                    CriadaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FinalizadaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TentativasPagamento", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TentativasPagamento_Pedidos_PedidoId",
                        column: x => x.PedidoId,
                        principalTable: "Pedidos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Pedidos_CheckoutSessionId",
                table: "Pedidos",
                column: "CheckoutSessionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Pedidos_CupomId",
                table: "Pedidos",
                column: "CupomId");

            migrationBuilder.CreateIndex(
                name: "IX_Pedidos_PaymentIdempotencyKey",
                table: "Pedidos",
                column: "PaymentIdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Cupons_Codigo",
                table: "Cupons",
                column: "Codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TentativasPagamento_PedidoId",
                table: "TentativasPagamento",
                column: "PedidoId");

            migrationBuilder.AddForeignKey(
                name: "FK_Pedidos_Cupons_CupomId",
                table: "Pedidos",
                column: "CupomId",
                principalTable: "Cupons",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Pedidos_Cupons_CupomId",
                table: "Pedidos");

            migrationBuilder.DropTable(
                name: "Cupons");

            migrationBuilder.DropTable(
                name: "TentativasPagamento");

            migrationBuilder.DropIndex(
                name: "IX_Pedidos_CheckoutSessionId",
                table: "Pedidos");

            migrationBuilder.DropIndex(
                name: "IX_Pedidos_CupomId",
                table: "Pedidos");

            migrationBuilder.DropIndex(
                name: "IX_Pedidos_PaymentIdempotencyKey",
                table: "Pedidos");

            migrationBuilder.DropColumn(
                name: "CheckoutHash",
                table: "Pedidos");

            migrationBuilder.DropColumn(
                name: "CheckoutSessionId",
                table: "Pedidos");

            migrationBuilder.DropColumn(
                name: "CupomCodigo",
                table: "Pedidos");

            migrationBuilder.DropColumn(
                name: "CupomId",
                table: "Pedidos");

            migrationBuilder.DropColumn(
                name: "PagamentoLeaseAte",
                table: "Pedidos");

            migrationBuilder.DropColumn(
                name: "PagamentoPayloadJson",
                table: "Pedidos");

            migrationBuilder.DropColumn(
                name: "PaymentIdempotencyKey",
                table: "Pedidos");

            migrationBuilder.DropColumn(
                name: "ValorDesconto",
                table: "Pedidos");
        }
    }
}

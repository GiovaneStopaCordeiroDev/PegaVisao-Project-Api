using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PegaVisaoApi.Migrations
{
    /// <inheritdoc />
    public partial class AdicionarAvaliacoesProdutos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AvaliacoesProdutos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProdutoId = table.Column<int>(type: "integer", nullable: false),
                    UsuarioId = table.Column<int>(type: "integer", nullable: false),
                    Nota = table.Column<int>(type: "integer", nullable: false),
                    AtualizadaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AvaliacoesProdutos", x => x.Id);
                    table.CheckConstraint("CK_Avaliacao_Nota", "\"Nota\" BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "FK_AvaliacoesProdutos_Produtos_ProdutoId",
                        column: x => x.ProdutoId,
                        principalTable: "Produtos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AvaliacoesProdutos_Usuarios_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AvaliacoesProdutos_ProdutoId_UsuarioId",
                table: "AvaliacoesProdutos",
                columns: new[] { "ProdutoId", "UsuarioId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AvaliacoesProdutos_UsuarioId",
                table: "AvaliacoesProdutos",
                column: "UsuarioId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AvaliacoesProdutos");
        }
    }
}

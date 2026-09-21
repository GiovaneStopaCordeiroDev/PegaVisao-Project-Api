using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PegaVisaoApi.Migrations
{
    /// <inheritdoc />
    public partial class AdicionarConexaoMelhorEnvio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MelhorEnvioConexoes",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AccessTokenProtegido = table.Column<string>(type: "text", nullable: true),
                    RefreshTokenProtegido = table.Column<string>(type: "text", nullable: true),
                    ExpiraEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AtualizadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StateHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    NavegadorHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    StateExpiraEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AdministradorId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MelhorEnvioConexoes", x => x.Id);
                });

            // Tokens da loja não devem ficar acessíveis pela API pública do Supabase.
            // O backend usa a conexão PostgreSQL privilegiada, fora dos papéis anon/authenticated.
            migrationBuilder.Sql("""ALTER TABLE "MelhorEnvioConexoes" ENABLE ROW LEVEL SECURITY;""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MelhorEnvioConexoes");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PegaVisaoApi.Migrations
{
    /// <inheritdoc />
    public partial class AdicionarRecuperacaoSenhaEGoogle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GoogleSubject",
                table: "Usuarios",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RecuperacaoEnviadaEm",
                table: "Usuarios",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RecuperacaoExpiraEm",
                table: "Usuarios",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecuperacaoHash",
                table: "Usuarios",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VersaoSessao",
                table: "Usuarios",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Usuarios_GoogleSubject",
                table: "Usuarios",
                column: "GoogleSubject",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Usuarios_GoogleSubject",
                table: "Usuarios");

            migrationBuilder.DropColumn(
                name: "GoogleSubject",
                table: "Usuarios");

            migrationBuilder.DropColumn(
                name: "RecuperacaoEnviadaEm",
                table: "Usuarios");

            migrationBuilder.DropColumn(
                name: "RecuperacaoExpiraEm",
                table: "Usuarios");

            migrationBuilder.DropColumn(
                name: "RecuperacaoHash",
                table: "Usuarios");

            migrationBuilder.DropColumn(
                name: "VersaoSessao",
                table: "Usuarios");
        }
    }
}

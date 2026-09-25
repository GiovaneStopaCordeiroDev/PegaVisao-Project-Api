using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PegaVisaoApi.Data;

#nullable disable

namespace PegaVisaoApi.Migrations;

[DbContext(typeof(PegaVisaoContext))]
[Migration("20260925060000_AdicionarContatoDestinatarioPedido")]
public partial class AdicionarContatoDestinatarioPedido : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "CpfDestinatario",
            table: "Pedidos",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "TelefoneDestinatario",
            table: "Pedidos",
            type: "text",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "CpfDestinatario", table: "Pedidos");
        migrationBuilder.DropColumn(name: "TelefoneDestinatario", table: "Pedidos");
    }
}

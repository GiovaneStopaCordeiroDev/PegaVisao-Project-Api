using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PegaVisaoApi.Data;

#nullable disable

namespace PegaVisaoApi.Migrations;

[DbContext(typeof(PegaVisaoContext))]
[Migration("20260924194000_AdicionarEtiquetaMelhorEnvio")]
public partial class AdicionarEtiquetaMelhorEnvio : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "MelhorEnvioEtiquetaGeradaEm",
            table: "Pedidos",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "MelhorEnvioEtiquetaStatus",
            table: "Pedidos",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "MelhorEnvioOrderId",
            table: "Pedidos",
            type: "text",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "MelhorEnvioEtiquetaGeradaEm", table: "Pedidos");
        migrationBuilder.DropColumn(name: "MelhorEnvioEtiquetaStatus", table: "Pedidos");
        migrationBuilder.DropColumn(name: "MelhorEnvioOrderId", table: "Pedidos");
    }
}

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PegaVisaoApi.Data;

#nullable disable

namespace PegaVisaoApi.Migrations;

[DbContext(typeof(PegaVisaoContext))]
[Migration("20260925160000_AdicionarRastreioMelhorEnvio")]
public partial class AdicionarRastreioMelhorEnvio : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "MelhorEnvioRastreioStatus",
            table: "Pedidos",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "MelhorEnvioRastreioAtualizadoEm",
            table: "Pedidos",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "MelhorEnvioTracking",
            table: "Pedidos",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "MelhorEnvioTrackingUrl",
            table: "Pedidos",
            type: "text",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_Pedidos_MelhorEnvioOrderId",
            table: "Pedidos",
            column: "MelhorEnvioOrderId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Pedidos_MelhorEnvioOrderId",
            table: "Pedidos");

        migrationBuilder.DropColumn(name: "MelhorEnvioRastreioStatus", table: "Pedidos");
        migrationBuilder.DropColumn(name: "MelhorEnvioRastreioAtualizadoEm", table: "Pedidos");
        migrationBuilder.DropColumn(name: "MelhorEnvioTracking", table: "Pedidos");
        migrationBuilder.DropColumn(name: "MelhorEnvioTrackingUrl", table: "Pedidos");
    }
}

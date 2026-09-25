using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PegaVisaoApi.Data;
using PegaVisaoApi.Services.MelhorEnvio;

namespace PegaVisaoApi.Controllers;

[ApiController]
[Route("api/MelhorEnvio/webhook")]
[AllowAnonymous]
public sealed class MelhorEnvioWebhookController(
    PegaVisaoContext db,
    IOptions<MelhorEnvioOptions> options,
    ILogger<MelhorEnvioWebhookController> logger) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Receber(CancellationToken ct)
    {
        Request.EnableBuffering();

        using var reader = new StreamReader(Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var corpo = await reader.ReadToEndAsync(ct);
        Request.Body.Position = 0;

        if (!Request.Headers.TryGetValue("X-ME-Signature", out var assinaturaRecebida) ||
            string.IsNullOrWhiteSpace(assinaturaRecebida))
            return Unauthorized();

        var secret = options.Value.ClientSecret;
        if (string.IsNullOrWhiteSpace(secret)) return StatusCode(503);

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var assinaturaEsperada = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(corpo)));

        byte[] recebida;
        byte[] esperada;
        try
        {
            recebida = Convert.FromBase64String(assinaturaRecebida.ToString().Trim());
            esperada = Convert.FromBase64String(assinaturaEsperada);
        }
        catch (FormatException)
        {
            return Unauthorized();
        }

        if (recebida.Length != esperada.Length ||
            !CryptographicOperations.FixedTimeEquals(recebida, esperada))
            return Unauthorized();

        JsonDocument documento;
        try
        {
            documento = JsonDocument.Parse(corpo);
        }
        catch (JsonException)
        {
            return BadRequest();
        }

        using (documento)
        {
            var raiz = documento.RootElement;
            if (!raiz.TryGetProperty("event", out var eventoProp) ||
                !raiz.TryGetProperty("data", out var dados) ||
                dados.ValueKind != JsonValueKind.Object)
                return BadRequest();

            var evento = eventoProp.GetString();
            var orderId = dados.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
            if (string.IsNullOrWhiteSpace(orderId))
                return BadRequest();

            var pedido = await db.Pedidos.SingleOrDefaultAsync(
                p => p.MelhorEnvioOrderId == orderId, ct);

            // Retorna 200 também quando a etiqueta não pertence mais à base local.
            if (pedido == null)
            {
                logger.LogInformation("Webhook Melhor Envio ignorado para etiqueta desconhecida.");
                return Ok();
            }

            var status = dados.TryGetProperty("status", out var statusProp) ? statusProp.GetString() : null;
            var tracking = dados.TryGetProperty("tracking", out var trackingProp) &&
                           trackingProp.ValueKind == JsonValueKind.String
                ? trackingProp.GetString()
                : null;
            var trackingUrl = dados.TryGetProperty("tracking_url", out var urlProp) &&
                              urlProp.ValueKind == JsonValueKind.String
                ? urlProp.GetString()
                : null;

            MelhorEnvioEtiquetaService.AplicarWebhook(pedido, status, tracking, trackingUrl);

            // O nome do evento é mantido como fallback caso o campo status venha vazio.
            if (string.IsNullOrWhiteSpace(status) && !string.IsNullOrWhiteSpace(evento) &&
                evento.StartsWith("order.", StringComparison.OrdinalIgnoreCase))
            {
                MelhorEnvioEtiquetaService.AplicarWebhook(
                    pedido,
                    evento["order.".Length..],
                    tracking,
                    trackingUrl);
            }

            await db.SaveChangesAsync(ct);
            return Ok();
        }
    }
}

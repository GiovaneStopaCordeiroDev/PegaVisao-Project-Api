using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PegaVisaoApi.Data;
using PegaVisaoApi.Models;
using PegaVisaoApi.Services;
using System.Text.Json;

namespace PegaVisaoApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MercadoPagoController : ControllerBase
{
    private readonly MercadoPagoService _mercadoPagoService;
    private readonly PegaVisaoContext _context;
    private readonly ILogger<MercadoPagoController> _logger;

    public MercadoPagoController(MercadoPagoService mercadoPagoService,
        PegaVisaoContext context, ILogger<MercadoPagoController> logger)
    {
        _mercadoPagoService = mercadoPagoService;
        _context = context;
        _logger = logger;
    }

    [HttpPost("webhook")]
    [AllowAnonymous]
    public async Task<IActionResult> Webhook([FromBody] JsonElement notificacao)
    {
        if (notificacao.ValueKind != JsonValueKind.Object ||
            !notificacao.TryGetProperty("type", out var tipo))
            return BadRequest();
        var type = tipo.ToString();
        if (type != "order" && type != "payment")
            return Ok();
        if (!notificacao.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Object || !data.TryGetProperty("id", out var id))
            return BadRequest();
        var resourceId = id.ToString();
        if (string.IsNullOrWhiteSpace(resourceId) ||
            (type == "order" ? !resourceId.StartsWith("ORD", StringComparison.Ordinal) ||
                !resourceId.All(char.IsAsciiLetterOrDigit) : !resourceId.All(char.IsAsciiDigit)))
            return BadRequest();

        _logger.LogInformation("MP webhook recebido: {Tipo} {ResourceId}", type, resourceId);
        try
        {
            // Não utilizamos status ou external_reference enviados no body.
            var estado = type == "order"
                ? EstadoPagamentoMercadoPago.DaOrder(await _mercadoPagoService.ConsultarOrderAsync(resourceId))
                : EstadoPagamentoMercadoPago.DoPayment(await _mercadoPagoService.ConsultarPagamentoAsync(resourceId));
            if ((estado.OrderId ?? estado.PaymentId) != resourceId)
                throw new InvalidDataException("A API retornou um recurso diferente do solicitado.");

            if (!int.TryParse(estado.ExternalReference, out var pedidoId))
            {
                _logger.LogWarning("MP recurso externo ignorado: {ResourceId}", resourceId);
                return Ok();
            }

            // Payments legados precisam de vínculo já persistido; Orders vinculam por ID/referência.
            if (type == "payment" && !await _context.Pedidos.AnyAsync(p =>
                p.Id == pedidoId && p.MercadoPagoPaymentId == estado.PaymentId))
                return Ok();
            await new EstoqueService(_context).AplicarPagamentoAsync(pedidoId, estado);
            return Ok();
        }
        catch (Exception ex)
        {
            // Inclui 404 do provedor: pode haver atraso na visibilidade do recurso.
            _logger.LogError(ex, "MP falha ao processar {ResourceId}; solicitar reenvio", resourceId);
            return StatusCode(503);
        }
    }

    // Recupera o vínculo quando a resposta da criação foi perdida.
    // O ID informado pelo administrador é sempre consultado e validado no provedor.
    [HttpPost("conciliar-estoque/{pedidoId:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> ConciliarEstoque(int pedidoId, [FromBody] ConciliarEstoqueDto dto)
    {
        if (!await _context.Pedidos.AnyAsync(p => p.Id == pedidoId)) return NotFound();
        try
        {
            var estado = EstadoPagamentoMercadoPago.DaOrder(
                await _mercadoPagoService.ConsultarOrderAsync(dto.OrderId));
            if (estado.OrderId != dto.OrderId)
                return Conflict(new { mensagem = "Recurso retornado diverge da order solicitada." });
            await new EstoqueService(_context).AplicarPagamentoAsync(pedidoId, estado);
            return Ok(new { mensagem = "Pagamento consultado e estoque conciliado.", estado.Confirmado,
                estado.EncerradoSemPagamento });
        }
        catch (ArgumentException) { return BadRequest(new { mensagem = "Informe um Order ID válido." }); }
        catch (InvalidDataException) { return Conflict(new { mensagem = "Order não corresponde ao pedido." }); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha na conciliação administrativa do pedido {PedidoId}", pedidoId);
            return StatusCode(503, new { mensagem = "Conciliação não concluída. A reserva foi preservada." });
        }
    }

    public sealed record ConciliarEstoqueDto(string OrderId);
    [HttpGet("teste-pagamento/{pedidoId:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> TestarPagamento(int pedidoId)
    {
        var pedido = await _context.Pedidos.AsNoTracking().SingleOrDefaultAsync(p => p.Id == pedidoId);
        if (pedido == null) return NotFound();
        if (string.IsNullOrWhiteSpace(pedido.MercadoPagoOrderId) &&
            (string.IsNullOrWhiteSpace(pedido.MercadoPagoPaymentId) || pedido.MercadoPagoPaymentId.StartsWith("PAY")))
            return BadRequest(new { mensagem = "Pedido sem Order ID. Recupere ORD... na resposta original ou reenvie a notificação order; PAY... não pode ser consultado na Payments API." });
        try
        {
            var estado = !string.IsNullOrWhiteSpace(pedido.MercadoPagoOrderId)
                ? EstadoPagamentoMercadoPago.DaOrder(await _mercadoPagoService.ConsultarOrderAsync(pedido.MercadoPagoOrderId))
                : EstadoPagamentoMercadoPago.DoPayment(await _mercadoPagoService.ConsultarPagamentoAsync(pedido.MercadoPagoPaymentId!));
            return Ok(new { pedidoId, estado.OrderId, estado.PaymentId, estado.Status,
                estado.StatusDetail, estado.Confirmado, statusPedido = pedido.Status.ToString() });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MP falha na consulta administrativa do pedido {PedidoId}", pedidoId);
            return StatusCode(502, new { mensagem = "Não foi possível consultar o recurso no Mercado Pago." });
        }
    }
}

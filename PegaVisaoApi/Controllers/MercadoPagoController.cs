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

            // Serializa webhooks concorrentes do mesmo pedido no PostgreSQL.
            await using var transaction = await _context.Database.BeginTransactionAsync();
            var pedido = await _context.Pedidos
                .FromSqlInterpolated($"SELECT * FROM \"Pedidos\" WHERE \"Id\" = {pedidoId} FOR UPDATE")
                .SingleOrDefaultAsync();
            if (pedido == null)
            {
                _logger.LogWarning("MP pedido inexistente: {PedidoId}, recurso {ResourceId}", pedidoId, resourceId);
                // O pedido é persistido antes da chamada ao provedor.
                return Ok();
            }
            if (pedido.ValorTotal != estado.Valor ||
                (pedido.MercadoPagoOrderId != null && pedido.MercadoPagoOrderId != estado.OrderId) ||
                (pedido.MercadoPagoPaymentId != null && pedido.MercadoPagoPaymentId != estado.PaymentId) ||
                (type == "payment" && pedido.MercadoPagoPaymentId == null))
            {
                _logger.LogError("MP divergência de vínculo ou valor: pedido {PedidoId}, recurso {ResourceId}", pedidoId, resourceId);
                return Ok(); // Erro permanente: investigar, sem confirmar nem repetir indefinidamente.
            }

            pedido.MercadoPagoOrderId = estado.OrderId;
            pedido.MercadoPagoPaymentId = estado.PaymentId;
            // Evita que uma consulta anterior à confirmação regrida o status.
            if (pedido.Status == Status.Pendente || estado.Status != "action_required" && estado.Status != "pending")
                pedido.MercadoPagoStatus = estado.Status;
            var transicao = estado.Confirmado && pedido.Status == Status.Pendente;
            if (transicao)
            {
                pedido.Status = Status.Pago;
                // Futura baixa de estoque: aqui, na mesma transação e sob o mesmo bloqueio.
                // Ainda não há baixa de estoque implementada no projeto.
            }
            if (estado.Confirmado && pedido.Status == Status.Cancelado)
                _logger.LogWarning("MP pagamento de pedido cancelado: {PedidoId}; requer conciliação", pedidoId);

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            _logger.LogInformation("MP confirmado na API: pedido {PedidoId}, order {OrderId}, payment {PaymentId}, status {Status}/{Detalhe}, transicaoPago {Transicao}",
                pedido.Id, estado.OrderId, estado.PaymentId, estado.Status, estado.StatusDetail, transicao);
            return Ok();
        }
        catch (Exception ex)
        {
            // Inclui 404 do provedor: pode haver atraso na visibilidade do recurso.
            _logger.LogError(ex, "MP falha ao processar {ResourceId}; solicitar reenvio", resourceId);
            return StatusCode(503);
        }
    }

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

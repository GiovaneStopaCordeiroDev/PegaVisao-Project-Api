using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PegaVisaoApi.Data;
using PegaVisaoApi.Services;
using System.Text.Json;

namespace PegaVisaoApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MercadoPagoController : ControllerBase
    {
        private readonly MercadoPagoService _mercadoPagoService;
        private readonly PegaVisaoContext _context;

        public MercadoPagoController(
            MercadoPagoService mercadoPagoService,
            PegaVisaoContext context)
        {
            _mercadoPagoService = mercadoPagoService;
            _context = context;
        }

        [HttpPost("webhook")]
        [AllowAnonymous]
        public async Task<IActionResult> Webhook(
            [FromBody] JsonElement notificacao)
        {
            try
            {
                Console.WriteLine("=================================");
                Console.WriteLine("WEBHOOK MERCADO PAGO RECEBIDO");
                Console.WriteLine(notificacao.ToString());
                Console.WriteLine("=================================");

                // Verifica se é uma notificação de pagamento
                if (!notificacao.TryGetProperty(
                    "type",
                    out var type))
                {
                    return Ok();
                }

                if (type.GetString() != "payment")
                {
                    return Ok();
                }

                // Pega o payment_id
                if (!notificacao.TryGetProperty(
                    "data",
                    out var data))
                {
                    return Ok();
                }

                if (!data.TryGetProperty(
                    "id",
                    out var id))
                {
                    return Ok();
                }

                var paymentId = id.ToString();

                Console.WriteLine(
                    $"Payment ID recebido: {paymentId}"
                );

                // Consulta o pagamento diretamente no Mercado Pago
                var pagamento =
                    await _mercadoPagoService
                        .ConsultarPagamentoAsync(paymentId);

                var status =
                    pagamento
                        .GetProperty("status")
                        .GetString();

                var statusDetail =
                    pagamento
                        .GetProperty("status_detail")
                        .GetString();

                Console.WriteLine(
                    $"Status Mercado Pago: {status}"
                );

                Console.WriteLine(
                    $"Status detalhe: {statusDetail}"
                );

                // Procura o pedido pelo Payment ID
                var pedido = await _context.Pedidos
                    .FirstOrDefaultAsync(p =>
                        p.MercadoPagoPaymentId == paymentId
                    );

                if (pedido == null)
                {
                    Console.WriteLine(
                        $"Pedido não encontrado para payment_id {paymentId}"
                    );

                    // Mesmo assim retornamos 200.
                    // O Mercado Pago não precisa reenviar
                    // indefinidamente uma notificação válida.
                    return Ok();
                }

                // Atualiza o status do Mercado Pago
                pedido.MercadoPagoStatus = status;

                await _context.SaveChangesAsync();

                Console.WriteLine(
                    $"Pedido {pedido.Id} atualizado para status {status}"
                );

                return Ok();
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    "ERRO NO WEBHOOK MERCADO PAGO:"
                );

                Console.WriteLine(ex);

                return Ok();
            }
        }
    }
}
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace PegaVisaoApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MercadoPagoController : ControllerBase
    {
        [HttpPost("webhook")]
        [AllowAnonymous]
        public IActionResult Webhook(
            [FromBody] JsonElement notificacao)
        {
            Console.WriteLine("=================================");
            Console.WriteLine("WEBHOOK MERCADO PAGO RECEBIDO");
            Console.WriteLine(notificacao.ToString());
            Console.WriteLine("=================================");

            if (notificacao.TryGetProperty(
                "data",
                out var data) &&
                data.TryGetProperty(
                    "id",
                    out var id))
            {
                var paymentId = id.ToString();

                Console.WriteLine(
                    $"Payment ID recebido: {paymentId}"
                );
            }

            return Ok();
        }
    }
}
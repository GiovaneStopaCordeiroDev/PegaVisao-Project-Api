using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace PegaVisaoApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MercadoPagoController : ControllerBase
    {
        [HttpPost("webhook")]
        [AllowAnonymous]
        public IActionResult Webhook([FromBody] object notificacao)
        {
            Console.WriteLine("Webhook Mercado Pago recebido:");
            Console.WriteLine(notificacao);

            return Ok();
        }
    }
}
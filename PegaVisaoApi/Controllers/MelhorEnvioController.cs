using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PegaVisaoApi.Services.MelhorEnvio;

namespace PegaVisaoApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class MelhorEnvioController(MelhorEnvioService service, ILogger<MelhorEnvioController> logger) : ControllerBase
{
    private const string CookieName = "__Secure-PegaVisaoMelhorEnvio";
    private static CookieOptions CookieConfig() => new()
    {
        HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax,
        Path = "/api/MelhorEnvio/callback", MaxAge = TimeSpan.FromMinutes(10), IsEssential = true
    };

    [HttpPost("autorizar")]
    public Task<IActionResult> Autorizar(CancellationToken ct) => ExecutarAsync(async () =>
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return Unauthorized();
        return Ok(new { url = await service.CriarAutorizacaoAsync(id, ct) });
    });

    [HttpGet("iniciar")]
    [AllowAnonymous]
    public Task<IActionResult> Iniciar([FromQuery] string ticket, CancellationToken ct) => ExecutarAsync(async () =>
    {
        var result = await service.IniciarAsync(ticket, ct);
        Response.Cookies.Append(CookieName, result.Cookie, CookieConfig());
        return Redirect(result.Url);
    });

    [HttpGet("callback")]
    [AllowAnonymous]
    public Task<IActionResult> Callback([FromQuery] string? state, [FromQuery] string? code,
        [FromQuery] string? error, CancellationToken ct) => ExecutarAsync(async () =>
    {
        await service.ConcluirAsync(state, Request.Cookies[CookieName], code, error, ct);
        Response.Cookies.Delete(CookieName, CookieConfig());
        return Content("Melhor Envio conectado com sucesso. Volte ao painel administrativo do PegaVisão e clique em Atualizar status.", "text/plain; charset=utf-8");
    });

    [HttpGet("status")]
    public Task<IActionResult> Status(CancellationToken ct) => ExecutarAsync(async () => Ok(await service.StatusAsync(ct)));

    [HttpPost("renovar")]
    public Task<IActionResult> Renovar(CancellationToken ct) => ExecutarAsync(async () =>
    {
        await service.ObterAccessTokenAsync(ct);
        return Ok(new { mensagem = "Conexão verificada; token renovado se próximo do vencimento." });
    });

    private async Task<IActionResult> ExecutarAsync(Func<Task<IActionResult>> action)
    {
        Response.Headers["Cache-Control"] = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        try { return await action(); }
        catch (MelhorEnvioException ex)
        {
            logger.LogWarning("Melhor Envio: operação não concluída (HTTP {Status})", ex.HttpStatus);
            return StatusCode(ex.HttpStatus, new { mensagem = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError("Melhor Envio: falha {Tipo}. Verifique banco, chave de criptografia e disponibilidade do provedor.", ex.GetType().Name);
            return StatusCode(503, new { mensagem = "Integração temporariamente indisponível. Verifique os logs; se a autorização falhou, inicie novamente pelo painel." });
        }
    }
}

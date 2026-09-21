using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PegaVisaoApi.DTO_s;
using PegaVisaoApi.Services.Frete;
using PegaVisaoApi.Services.MelhorEnvio;

namespace PegaVisaoApi.Controllers;

public class CotarFreteDto
{
    [Required] public string Cep { get; set; } = "";
    [Required, MinLength(1), MaxLength(50)] public List<CreateItemPedidoDto> Itens { get; set; } = [];
}

[ApiController, Authorize]
[Route("api/Frete")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class FreteController(FreteService service, ILogger<FreteController> logger) : ControllerBase
{
    [HttpPost("cotacoes")]
    public Task<IActionResult> Cotar(CotarFreteDto dto, CancellationToken ct) => Executar(async id =>
        await service.CotarAsync(id, dto.Cep, dto.Itens, ct));

    [HttpGet("cotacoes/{id:guid}")]
    public Task<IActionResult> Consultar(Guid id, CancellationToken ct) => Executar(async usuarioId =>
        await service.ConsultarAsync(id, usuarioId, ct));

    private async Task<IActionResult> Executar(Func<int, Task<object>> action)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return Unauthorized();
        try { return Ok(await action(id)); }
        catch (FreteException ex) { return StatusCode(ex.Status, new { mensagem = ex.Message }); }
        catch (MelhorEnvioException ex)
        {
            logger.LogWarning("Frete indisponível: conexão Melhor Envio, HTTP {Status}", ex.HttpStatus);
            return StatusCode(503, new { mensagem = "A loja precisa verificar a conexão com as transportadoras. Tente novamente mais tarde." });
        }
        catch (Exception ex)
        {
            logger.LogError("Frete indisponível: {Tipo}", ex.GetType().Name);
            return StatusCode(503, new { mensagem = "Não foi possível calcular o frete agora. Tente novamente em instantes." });
        }
    }
}

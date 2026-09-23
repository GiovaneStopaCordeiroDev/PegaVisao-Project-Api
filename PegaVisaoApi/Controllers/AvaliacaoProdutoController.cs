using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PegaVisaoApi.Data;
using PegaVisaoApi.Models;

namespace PegaVisaoApi.Controllers;

[ApiController]
[Route("Produto/{produtoId:int}/avaliacoes")]
public class AvaliacaoProdutoController(PegaVisaoContext context) : ControllerBase
{
    public record AvaliarDto([property: Range(1, 5)] int Nota);

    // A compra é conferida no servidor, nunca por dados enviados pelo navegador.
    private Task<bool> Comprou(int produtoId, int usuarioId, CancellationToken ct) =>
        context.ItemPedidos.AnyAsync(i => i.VariacaoProduto.ProdutoId == produtoId &&
            i.Pedido.UsuarioId == usuarioId &&
            (i.Pedido.Status == Status.Pago || i.Pedido.Status == Status.Enviado || i.Pedido.Status == Status.Entregue), ct);

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Resumo(int produtoId, CancellationToken ct)
    {
        if (!await context.Produtos.AnyAsync(p => p.Id == produtoId, ct)) return NotFound();
        var resumo = await context.AvaliacoesProdutos.AsNoTracking().Where(a => a.ProdutoId == produtoId)
            .GroupBy(a => a.ProdutoId)
            .Select(g => new { Total = g.Count(), Media = g.Average(a => a.Nota) }).SingleOrDefaultAsync(ct);
        return Ok(new { total = resumo?.Total ?? 0, media = resumo?.Media ?? 0 });
    }

    [HttpGet("minha")]
    [Authorize]
    public async Task<IActionResult> Minha(int produtoId, CancellationToken ct)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var usuarioId)) return Unauthorized();
        if (!await context.Produtos.AnyAsync(p => p.Id == produtoId, ct)) return NotFound();
        var nota = await context.AvaliacoesProdutos.Where(a => a.ProdutoId == produtoId && a.UsuarioId == usuarioId)
            .Select(a => (int?)a.Nota).SingleOrDefaultAsync(ct);
        return Ok(new { nota, podeAvaliar = await Comprou(produtoId, usuarioId, ct) });
    }

    [HttpPut("minha")]
    [Authorize]
    public async Task<IActionResult> Avaliar(int produtoId, AvaliarDto dto, CancellationToken ct)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var usuarioId)) return Unauthorized();
        if (dto.Nota is < 1 or > 5) return BadRequest(new { mensagem = "Escolha de 1 a 5 estrelas." });
        if (!await context.Produtos.AnyAsync(p => p.Id == produtoId, ct)) return NotFound();
        if (!await Comprou(produtoId, usuarioId, ct))
            return StatusCode(403, new { mensagem = "Somente quem comprou este produto com pagamento confirmado pode avaliar." });

        // Upsert atômico: requisições simultâneas não criam avaliações duplicadas.
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "AvaliacoesProdutos" ("ProdutoId", "UsuarioId", "Nota", "AtualizadaEm")
            VALUES ({produtoId}, {usuarioId}, {dto.Nota}, {DateTime.UtcNow})
            ON CONFLICT ("ProdutoId", "UsuarioId") DO UPDATE
            SET "Nota" = EXCLUDED."Nota", "AtualizadaEm" = EXCLUDED."AtualizadaEm"
            """, ct);
        return NoContent();
    }
}

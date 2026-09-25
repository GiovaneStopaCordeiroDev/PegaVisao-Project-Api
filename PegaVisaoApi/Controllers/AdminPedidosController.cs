using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PegaVisaoApi.Data;
using PegaVisaoApi.Models;
using PegaVisaoApi.Services.MelhorEnvio;

namespace PegaVisaoApi.Controllers;

[ApiController]
[Route("api/admin/pedidos")]
[Authorize(Roles = "Admin")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminPedidosController(PegaVisaoContext db, MelhorEnvioEtiquetaService etiquetaService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Listar(string filtro = "todos", int pagina = 1,
        int tamanhoPagina = 20, CancellationToken ct = default)
    {
        if (pagina < 1 || pagina > 100000 || tamanhoPagina is < 1 or > 50)
            return BadRequest(new { mensagem = "Paginação inválida." });
        var query = db.Pedidos.AsNoTracking();
        switch (filtro)
        {
            case "todos": break;
            case "pendentes": query = query.Where(p => p.Status == Status.Pendente); break;
            case "concluidos": query = query.Where(p => p.Status == Status.Pago ||
                p.Status == Status.Enviado || p.Status == Status.Entregue); break;
            case "cancelados": query = query.Where(p => p.Status == Status.Cancelado); break;
            default: return BadRequest(new { mensagem = "Filtro inválido." });
        }
        var total = await query.CountAsync(ct);
        var pedidos = await query.OrderByDescending(p => p.DataPedido).ThenByDescending(p => p.Id)
            .Skip((pagina - 1) * tamanhoPagina).Take(tamanhoPagina)
            .Select(p => new {
                p.Id, p.DataPedido, Status = p.Status.ToString(),
                Cliente = new { p.Usuario.Nome, p.Usuario.Email },
                p.FormaPagamento, p.ValorTotal, p.ValorFrete,
                SubtotalProdutos = p.ValorTotal - p.ValorFrete,
                p.FreteServico, p.FreteTransportadora, p.FretePrazoDias, p.FreteServicoId,
                p.PagamentoExpiraEm, p.MelhorEnvioOrderId, p.MelhorEnvioEtiquetaStatus,
                p.MelhorEnvioEtiquetaGeradaEm,
                p.MelhorEnvioTracking, p.MelhorEnvioTrackingUrl,
                p.MelhorEnvioRastreioStatus, p.MelhorEnvioRastreioAtualizadoEm,
                TemCpfDestinatario = p.CpfDestinatario != null,
                TemTelefoneDestinatario = p.TelefoneDestinatario != null,
                Endereco = new { p.Cep, p.Rua, p.Numero, p.Complemento, p.Bairro, p.Cidade, p.Estado },
                Itens = p.Itens.OrderBy(i => i.Id).Select(i => new {
                    i.Id, Nome = i.VariacaoProduto.Produto.Nome,
                    Imagem = i.VariacaoProduto.Produto.ImagemPrincipal,
                    i.VariacaoProduto.Cor, i.VariacaoProduto.Tamanho, i.Quantidade,
                    i.PrecoUnitario
                }).ToList()
            }).ToListAsync(ct);
        return Ok(new { pedidos, total, pagina, tamanhoPagina,
            totalPaginas = (int)Math.Ceiling(total / (double)tamanhoPagina) });
    }

    [HttpPost("{id:int}/etiqueta")]
    public async Task<IActionResult> GerarEtiqueta(int id, GerarEtiquetaAdminRequest request, CancellationToken ct)
    {
        try
        {
            return Ok(await etiquetaService.GerarAsync(id, request, ct));
        }
        catch (MelhorEnvioException ex)
        {
            return StatusCode(ex.HttpStatus, new { mensagem = ex.Message });
        }
    }

    [HttpPost("{id:int}/rastreio/atualizar")]
    public async Task<IActionResult> AtualizarRastreio(int id, CancellationToken ct)
    {
        try
        {
            return Ok(await etiquetaService.AtualizarRastreioAsync(id, ct));
        }
        catch (MelhorEnvioException ex)
        {
            return StatusCode(ex.HttpStatus, new { mensagem = ex.Message });
        }
    }

    [HttpGet("{id:int}/etiqueta/impressao")]
    public async Task<IActionResult> ImprimirEtiqueta(int id, CancellationToken ct)
    {
        try
        {
            return Ok(new { url = await etiquetaService.ObterLinkImpressaoAsync(id, ct) });
        }
        catch (MelhorEnvioException ex)
        {
            return StatusCode(ex.HttpStatus, new { mensagem = ex.Message });
        }
    }
}

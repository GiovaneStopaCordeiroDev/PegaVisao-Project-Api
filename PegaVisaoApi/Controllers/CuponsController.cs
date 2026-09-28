using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PegaVisaoApi.Data;
using PegaVisaoApi.DTO_s;
using PegaVisaoApi.Models;
using PegaVisaoApi.Services;
using PegaVisaoApi.Services.Frete;
namespace PegaVisaoApi.Controllers;

[ApiController, Route("api/cupons"), Authorize(Roles = "Admin")]
public class CuponsController(PegaVisaoContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Listar(CancellationToken ct) => Ok(await db.Cupons.AsNoTracking().Include(c => c.Produtos).Where(c => !c.Excluido)
        .OrderByDescending(c => c.Id).Select(c => new { cupom = c,
            usos = db.Pedidos.Count(p => p.CupomId == c.Id && p.Status != Status.Cancelado),
            pagos = db.Pedidos.Count(p => p.CupomId == c.Id && (p.Status == Status.Pago || p.Status == Status.Enviado || p.Status == Status.Entregue)) }).ToListAsync(ct));
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Buscar(int id, CancellationToken ct) =>
        await db.Cupons.AsNoTracking().Include(c => c.Produtos).SingleOrDefaultAsync(c => c.Id == id && !c.Excluido, ct) is { } c ? Ok(c) : NotFound();
    [HttpPost]
    public Task<IActionResult> Criar(CupomDto dto, CancellationToken ct) => Salvar(null, dto, ct);
    [HttpPut("{id:int}")]
    public Task<IActionResult> Editar(int id, CupomDto dto, CancellationToken ct) => Salvar(id, dto, ct);
    private async Task<IActionResult> Salvar(int? id, CupomDto dto, CancellationToken ct)
    {
        var ids = dto.TodosProdutos ? Array.Empty<int>() : dto.ProdutoIds.Distinct().ToArray();
        if (await db.Produtos.CountAsync(p => ids.Contains(p.Id), ct) != ids.Length)
            return BadRequest(new { mensagem = "Um produto selecionado não está mais disponível." });
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Usa o mesmo bloqueio da criação de pedidos para editar a regra e seus produtos atomicamente.
        var c = id.HasValue ? await db.Cupons.FromSqlInterpolated($"SELECT * FROM \"Cupons\" WHERE \"Id\" = {id.Value} FOR UPDATE").SingleOrDefaultAsync(c => c.Id == id && !c.Excluido, ct) : new Cupom();
        if (c == null) return NotFound();
        if (id.HasValue) await db.Entry(c).Collection(x => x.Produtos).LoadAsync(ct);
        c.TodosProdutos = dto.TodosProdutos;
        var removidos = c.Produtos.Where(p => !ids.Contains(p.ProdutoId)).ToList();
        db.RemoveRange(removidos);
        c.Produtos.RemoveAll(p => removidos.Contains(p));
        foreach (var produtoId in ids.Except(c.Produtos.Select(p => p.ProdutoId)).ToArray())
            c.Produtos.Add(new CupomProduto { ProdutoId = produtoId });
        c.Codigo = CupomService.Normalizar(dto.Codigo); c.Descricao = (dto.Descricao ?? "").Trim(); c.Tipo = dto.Tipo;
        c.Valor = dto.Valor; c.ValorMinimo = dto.ValorMinimo; c.DescontoMaximo = dto.DescontoMaximo;
        c.InicioEm = dto.InicioEm; c.ValidadeEm = dto.ValidadeEm; c.Ativo = dto.Ativo;
        c.LimiteTotal = dto.LimiteTotal; c.LimitePorUsuario = dto.LimitePorUsuario; c.AtualizadoEm = DateTime.UtcNow;
        if (!id.HasValue) db.Cupons.Add(c);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { return Conflict(new { mensagem = "Código já cadastrado, inclusive em cupons excluídos." }); }
        await tx.CommitAsync(ct);
        return id.HasValue ? Ok(c) : CreatedAtAction(nameof(Buscar), new { id = c.Id }, c);
    }
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Excluir(int id, CancellationToken ct)
    {
        var n = await db.Cupons.Where(c => c.Id == id && !c.Excluido).ExecuteUpdateAsync(u =>
            u.SetProperty(c => c.Excluido, true).SetProperty(c => c.Ativo, false).SetProperty(c => c.AtualizadoEm, DateTime.UtcNow), ct);
        return n == 0 ? NotFound() : NoContent();
    }
}

[ApiController, Route("api/cupons"), Authorize]
public class ValidacaoCupomController(PegaVisaoContext db, FreteService frete) : ControllerBase
{
    [HttpPost("validar")]
    public async Task<IActionResult> Validar(ValidarCupomDto dto, CancellationToken ct)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var usuarioId)) return Unauthorized();
        try {
            var itens = await frete.PrepararCarrinhoAsync(dto.Itens, true, ct);
            var subtotal = itens.Sum(i => i.Preco * i.Quantidade);
            var (cupom, desconto) = await new CupomService(db).ValidarAsync(dto.Codigo, itens, usuarioId, false, ct);
            return Ok(new { codigo = cupom.Codigo, subtotal, desconto, totalProdutos = subtotal - desconto, mensagem = "Cupom aplicado com sucesso." });
        } catch (FreteException e) { return StatusCode(e.Status, new { mensagem = e.Message }); }
    }
}

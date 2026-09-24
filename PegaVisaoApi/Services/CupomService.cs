using Microsoft.EntityFrameworkCore;
using PegaVisaoApi.Data;
using PegaVisaoApi.Models;
using PegaVisaoApi.Services.Frete;
namespace PegaVisaoApi.Services;

public sealed class CupomService(PegaVisaoContext db)
{
    public static string Normalizar(string? codigo) => (codigo ?? "").Trim().ToUpperInvariant();
    public static decimal Calcular(Cupom cupom, decimal subtotal, DateTime agora)
    {
        if (!cupom.Ativo || cupom.Excluido) throw new FreteException(422, "Cupom inativo.");
        if (agora < cupom.InicioEm) throw new FreteException(422, "Este cupom ainda não está disponível.");
        if (agora >= cupom.ValidadeEm) throw new FreteException(422, "Cupom expirado.");
        if (subtotal < cupom.ValorMinimo) throw new FreteException(422, $"Compra mínima para este cupom: R$ {cupom.ValorMinimo:F2}.");
        var desconto = cupom.Tipo == "Percentual" ? subtotal * cupom.Valor / 100m : cupom.Valor;
        desconto = Math.Min(desconto, cupom.DescontoMaximo ?? desconto);
        return decimal.Round(Math.Clamp(desconto, 0, subtotal), 2, MidpointRounding.AwayFromZero);
    }
    public async Task<(Cupom Cupom, decimal Desconto)> ValidarAsync(string codigo, decimal subtotal, int usuarioId, bool bloquear, CancellationToken ct)
    {
        codigo = Normalizar(codigo);
        var cupom = bloquear
            ? await db.Cupons.FromSqlInterpolated($"SELECT * FROM \"Cupons\" WHERE \"Codigo\" = {codigo} FOR UPDATE").SingleOrDefaultAsync(ct)
            : await db.Cupons.AsNoTracking().SingleOrDefaultAsync(c => c.Codigo == codigo, ct);
        if (cupom == null || cupom.Excluido) throw new FreteException(422, "Cupom inválido.");
        var desconto = Calcular(cupom, subtotal, DateTime.UtcNow);
        var usos = db.Pedidos.Where(p => p.CupomId == cupom.Id && p.Status != Status.Cancelado);
        if (cupom.LimiteTotal.HasValue && await usos.CountAsync(ct) >= cupom.LimiteTotal ||
            cupom.LimitePorUsuario.HasValue && await usos.CountAsync(p => p.UsuarioId == usuarioId, ct) >= cupom.LimitePorUsuario)
            throw new FreteException(422, "Limite de utilizações do cupom atingido.");
        return (cupom, desconto);
    }
}

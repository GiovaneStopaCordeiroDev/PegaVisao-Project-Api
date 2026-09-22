using Microsoft.EntityFrameworkCore;
using PegaVisaoApi.Data;
using PegaVisaoApi.Models;
using PegaVisaoApi.Services.Frete;

namespace PegaVisaoApi.Services;

// Cada operação participa da transação do chamador. O pedido deve estar bloqueado
// nas transições; as variações são atualizadas na mesma ordem para evitar deadlocks.
public sealed class EstoqueService(PegaVisaoContext db)
{
    public async Task ReservarAsync(Pedido pedido, CancellationToken ct = default)
    {
        ExigirTransacao();
        if (pedido.EstadoEstoque != EstadoEstoquePedido.Legado || pedido.Id != 0)
            throw new InvalidOperationException("Reserva permitida somente na criação do pedido.");
        var itens = Quantidades(pedido);
        foreach (var item in itens)
        {
            var afetadas = await db.VariacaoProdutos
                .Where(v => v.Id == item.Id && v.Estoque - v.EstoqueReservado >= item.Quantidade)
                .ExecuteUpdateAsync(u => u.SetProperty(v => v.EstoqueReservado,
                    v => v.EstoqueReservado + item.Quantidade), ct);
            if (afetadas != 1)
                throw new FreteException(409, "Estoque insuficiente. Atualize o carrinho antes de pagar.");
        }
        pedido.EstadoEstoque = EstadoEstoquePedido.Reservado;
        pedido.ProximaConsultaEstoqueEm = DateTime.UtcNow.AddMinutes(1);
    }

    private async Task FinalizarAsync(Pedido pedido, bool pago, CancellationToken ct = default)
    {
        ExigirTransacao();
        if (pedido.EstadoEstoque != EstadoEstoquePedido.Reservado) return;
        await db.Entry(pedido).Collection(p => p.Itens).LoadAsync(ct);
        foreach (var item in Quantidades(pedido))
        {
            var query = db.VariacaoProdutos.Where(v => v.Id == item.Id &&
                v.EstoqueReservado >= item.Quantidade && v.Estoque >= item.Quantidade);
            var afetadas = pago
                ? await query.ExecuteUpdateAsync(u => u
                    .SetProperty(v => v.Estoque, v => v.Estoque - item.Quantidade)
                    .SetProperty(v => v.EstoqueReservado, v => v.EstoqueReservado - item.Quantidade), ct)
                : await query.ExecuteUpdateAsync(u => u
                    .SetProperty(v => v.EstoqueReservado, v => v.EstoqueReservado - item.Quantidade), ct);
            if (afetadas != 1) throw new InvalidOperationException("Reserva inconsistente; requer conciliação.");
        }
        pedido.EstadoEstoque = pago ? EstadoEstoquePedido.Baixado : EstadoEstoquePedido.Liberado;
        pedido.ProximaConsultaEstoqueEm = null;
    }

    public async Task AplicarPagamentoAsync(int pedidoId, EstadoPagamentoMercadoPago estado,
        CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var pedido = await db.Pedidos
            .FromSqlInterpolated($"SELECT * FROM \"Pedidos\" WHERE \"Id\" = {pedidoId} FOR UPDATE")
            .SingleOrDefaultAsync(ct);
        if (pedido == null) return;
        if (estado.ExternalReference != pedido.Id.ToString() || pedido.ValorTotal != estado.Valor ||
            pedido.MercadoPagoOrderId != null && pedido.MercadoPagoOrderId != estado.OrderId ||
            pedido.MercadoPagoPaymentId != null && estado.PaymentId.Length > 0 &&
                pedido.MercadoPagoPaymentId != estado.PaymentId)
            throw new InvalidDataException("Vínculo ou valor do pagamento divergente.");

        pedido.MercadoPagoOrderId ??= estado.OrderId;
        if (estado.PaymentId.Length > 0) pedido.MercadoPagoPaymentId ??= estado.PaymentId;
        if (estado.Confirmado)
        {
            if (pedido.EstadoEstoque == EstadoEstoquePedido.Liberado ||
                pedido.Status == Status.Cancelado)
                throw new InvalidOperationException("Pagamento após cancelamento; requer conciliação.");
            await FinalizarAsync(pedido, true, ct);
            if (pedido.Status == Status.Pendente) pedido.Status = Status.Pago;
        }
        else if (estado.EncerradoSemPagamento)
        {
            await FinalizarAsync(pedido, false, ct);
            if (pedido.Status == Status.Pendente) pedido.Status = Status.Cancelado;
        }
        if (pedido.Status == Status.Pendente || estado.Confirmado || estado.EncerradoSemPagamento && pedido.Status == Status.Cancelado)
            pedido.MercadoPagoStatus = estado.Status;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private void ExigirTransacao()
    {
        if (db.Database.CurrentTransaction == null)
            throw new InvalidOperationException("Operação de estoque exige transação.");
    }

    private static List<(int Id, int Quantidade)> Quantidades(Pedido pedido)
    {
        if (pedido.Itens.Count == 0 || pedido.Itens.Any(i => i.Quantidade <= 0))
            throw new InvalidOperationException("Pedido sem itens válidos.");
        return pedido.Itens.GroupBy(i => i.VariacaoProdutoId).OrderBy(g => g.Key)
            .Select(g => (g.Key, g.Sum(i => i.Quantidade))).ToList();
    }
}

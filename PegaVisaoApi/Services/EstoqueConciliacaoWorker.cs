using Microsoft.EntityFrameworkCore;
using PegaVisaoApi.Data;
using PegaVisaoApi.Models;

namespace PegaVisaoApi.Services;

// O relógio local agenda consultas, mas nunca libera estoque sozinho.
public sealed class EstoqueConciliacaoWorker(IServiceScopeFactory scopes,
    ILogger<EstoqueConciliacaoWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<PegaVisaoContext>();
                var ids = await db.Pedidos.AsNoTracking()
                    .Where(p => p.EstadoEstoque == EstadoEstoquePedido.Reservado &&
                        p.ProximaConsultaEstoqueEm <= DateTime.UtcNow)
                    .OrderBy(p => p.ProximaConsultaEstoqueEm).Select(p => p.Id)
                    .Take(100).ToListAsync(stoppingToken);
                foreach (var id in ids)
                {
                    try
                    {
                        using var itemScope = scopes.CreateScope();
                        var itemDb = itemScope.ServiceProvider.GetRequiredService<PegaVisaoContext>();
                        // Lease simples compartilhada pelas instâncias do serviço.
                        var claimed = await itemDb.Pedidos.Where(p => p.Id == id &&
                            p.EstadoEstoque == EstadoEstoquePedido.Reservado &&
                            p.ProximaConsultaEstoqueEm <= DateTime.UtcNow)
                            .ExecuteUpdateAsync(u => u.SetProperty(p => p.ProximaConsultaEstoqueEm,
                                DateTime.UtcNow.AddMinutes(2)), stoppingToken);
                        if (claimed == 0) continue;
                        var pedido = await itemDb.Pedidos.AsNoTracking().SingleAsync(p => p.Id == id, stoppingToken);
                        if (pedido.MercadoPagoOrderId == null)
                        {
                            logger.LogWarning("Reserva {PedidoId} sem ID do provedor; mantida para conciliação manual", id);
                            continue;
                        }
                        var mp = itemScope.ServiceProvider.GetRequiredService<MercadoPagoService>();
                        var estado = EstadoPagamentoMercadoPago.DaOrder(
                            await mp.ConsultarOrderAsync(pedido.MercadoPagoOrderId));
                        await itemScope.ServiceProvider.GetRequiredService<EstoqueService>()
                            .AplicarPagamentoAsync(id, estado, stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Falha na conciliação da reserva {PedidoId}; estoque permanece protegido", id);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { logger.LogError(ex, "Falha no ciclo de conciliação de estoque"); }
        }
    }
}

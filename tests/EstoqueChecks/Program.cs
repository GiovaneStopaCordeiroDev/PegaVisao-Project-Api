using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PegaVisaoApi.Data;
using PegaVisaoApi.Models;
using PegaVisaoApi.Services;
using PegaVisaoApi.Services.Frete;

var count = 0;
void Check(bool ok, string name)
{
    if (!ok) throw new Exception(name);
    Console.WriteLine("OK: " + name); count++;
}
JsonElement Order(string status, bool empty = false, string paymentStatus = "canceled") =>
    JsonSerializer.SerializeToElement(new {
        id = "ORDTEST", external_reference = "1", total_amount = "10.00",
        status, status_detail = "test",
        transactions = new { payments = empty ? Array.Empty<object>() :
            new object[] { new { id = "PAYTEST", amount = "10.00",
                status = paymentStatus, status_detail = "test" } } }
    });
Check(EstadoPagamentoMercadoPago.DaOrder(Order("expired", true)).EncerradoSemPagamento, "Expiração sem transação libera reserva");
Check(EstadoPagamentoMercadoPago.DaOrder(Order("canceled")).EncerradoSemPagamento, "Cancelamento confirmado libera reserva");
Check(!EstadoPagamentoMercadoPago.DaOrder(Order("action_required", false, "failed")).EncerradoSemPagamento, "Tentativa rejeitada não libera checkout aberto");
Check(!EstadoPagamentoMercadoPago.DaOrder(Order("created", true)).Confirmado, "Checkout criado sem transação permanece pendente");
Check(!EstadoPagamentoMercadoPago.DaOrder(Order("refunded", false, "refunded")).EncerradoSemPagamento, "Reembolso não repõe automaticamente estoque físico");
var variant = new VariacaoProduto { Estoque = 10, EstoqueReservado = 2, Produto = new() { Preco = 10 } };
Check(variant.EstoqueDisponivel == 8, "Disponível desconta reservas");
try { FreteRegras.PrepararItem(variant, 9, false); throw new Exception("Aceitou quantidade indisponível"); }
catch (FreteException e) { Check(e.Status == 409, "Frete rejeita quantidade indisponível"); }

var connectionString = Environment.GetEnvironmentVariable("ESTOQUE_TEST_CONNECTION");
if (string.IsNullOrEmpty(connectionString))
{
    Console.WriteLine($"{count} verificações aprovadas. Integração NÃO executada: configure ESTOQUE_TEST_CONNECTION para banco local de testes.");
    return;
}
var builder = new NpgsqlConnectionStringBuilder(connectionString);
if (builder.Host is not ("localhost" or "127.0.0.1") || builder.Database != "estoque_tests")
    throw new Exception("Testes exigem PostgreSQL local e banco estoque_tests.");
var schema = "estoque_test_" + Guid.NewGuid().ToString("N");
await using var admin = new NpgsqlConnection(builder.ConnectionString);
await admin.OpenAsync();
await using (var create = new NpgsqlCommand($"CREATE DATABASE {schema}", admin))
    await create.ExecuteNonQueryAsync();
builder.Database = schema;
var options = new DbContextOptionsBuilder<PegaVisaoContext>()
    .UseNpgsql(builder.ConnectionString).Options;
PegaVisaoContext Db() => new(options);
await using var setup = Db();

try
{
    await setup.Database.MigrateAsync();
    Check(!(await setup.Database.GetPendingMigrationsAsync()).Any(), "Migrations aplicadas no banco isolado");
    var user = new Usuario { Nome = "Teste", Email = "estoque@example.invalid", SenhaHash = "fake" };
    var product = new Produto { Nome = "Teste", Descricao = "Teste", ImagemPrincipal = "",
        Preco = 10, Categoria = new Categoria { Nome = "Teste" } };
    var v = new VariacaoProduto { Produto = product, Cor = "Preto", Tamanho = "G", Estoque = 10 };
    var second = new VariacaoProduto { Produto = product, Cor = "Amarelo", Tamanho = "P", Estoque = 0 };
    setup.AddRange(user, v, second);
    await setup.SaveChangesAsync();
    var variationId = v.Id;
    var userId = user.Id;
    Pedido NewOrder(params (int Id, int Qty)[] items) => new() {
        UsuarioId = userId, ValorTotal = items.Sum(x => x.Qty) * 10,
        Cep = "17013113", Rua = "Teste", Numero = "1", Bairro = "Teste", Cidade = "Bauru",
        Estado = "SP", FormaPagamento = "Pix",
        Itens = items.Select(x => new ItemPedido { VariacaoProdutoId = x.Id, Quantidade = x.Qty, PrecoUnitario = 10 }).ToList()
    };
    async Task<int> Reserve(Pedido p) {
        await using var db = Db();
        await using var tx = await db.Database.BeginTransactionAsync();
        await new EstoqueService(db).ReservarAsync(p);
        db.Pedidos.Add(p);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return p.Id;
    }
    async Task AssertStock(int physical, int reserved, string name) {
        await using var db = Db();
        var actual = await db.VariacaoProdutos.AsNoTracking().SingleAsync(x => x.Id == variationId);
        Check(actual.Estoque == physical && actual.EstoqueReservado == reserved, name);
    }
    EstadoPagamentoMercadoPago State(int id, decimal amount, bool paid = true) =>
        new("ORD" + id, "PAY" + id, id.ToString(), paid ? "processed" : "canceled", "accredited", amount, paid)
            { EncerradoSemPagamento = !paid };
    async Task Apply(int id, EstadoPagamentoMercadoPago state) {
        await using var db = Db();
        await new EstoqueService(db).AplicarPagamentoAsync(id, state);
    }
    var paidId = await Reserve(NewOrder((variationId, 1), (variationId, 1)));
    await AssertStock(10, 2, "Agrupa itens repetidos e reserva quantidade total");
    await Task.WhenAll(Apply(paidId, State(paidId, 20)), Apply(paidId, State(paidId, 20)));
    await AssertStock(8, 0, "Webhooks concorrentes baixam estoque uma única vez");
    await Apply(paidId, State(paidId, 20, false));
    await AssertStock(8, 0, "Cancelamento atrasado não repõe uma venda confirmada");

    var cancelId = await Reserve(NewOrder((variationId, 2)));
    await Task.WhenAll(Apply(cancelId, State(cancelId, 20, false)), Apply(cancelId, State(cancelId, 20, false)));
    await AssertStock(8, 0, "Cancelamento repetido libera uma única vez");
    try { await Apply(cancelId, State(cancelId, 20)); throw new Exception("Confirmou reserva liberada"); }
    catch (InvalidOperationException) { Check(true, "Pagamento após liberação exige conciliação"); }

    try { await Reserve(NewOrder((variationId, 2), (second.Id, 1))); throw new Exception("Aceitou falta parcial"); }
    catch (FreteException) { await AssertStock(8, 0, "Falta em segundo item reverte toda reserva"); }

    var pendingId = await Reserve(NewOrder((variationId, 1)));
    await Apply(pendingId, State(pendingId, 10) with { Confirmado = false, Status = "action_required" });
    await AssertStock(8, 1, "Pagamento pendente mantém reserva");
    try { await Apply(pendingId, State(pendingId, 99)); throw new Exception("Aceitou valor adulterado"); }
    catch (InvalidDataException) { await AssertStock(8, 1, "Valor divergente não altera estoque"); }
    await Apply(pendingId, State(pendingId, 10, false));

    await setup.VariacaoProdutos.Where(x => x.Id == variationId).ExecuteUpdateAsync(u => u.SetProperty(x => x.Estoque, 1));
    async Task<bool> TryLast() {
        try { await Reserve(NewOrder((variationId, 1))); return true; }
        catch (FreteException e) when(e.Status == 409) { return false; }
    }
    var buyers = await Task.WhenAll(TryLast(), TryLast());
    Check(buyers.Count(x => x) == 1, "Somente um comprador reserva a última unidade");
    await AssertStock(1, 1, "Reserva concorrente nunca excede o estoque");

    await using (var stale = Db()) {
        var original = await stale.VariacaoProdutos.SingleAsync(x => x.Id == variationId);
        await setup.VariacaoProdutos.Where(x => x.Id == variationId).ExecuteUpdateAsync(u => u.SetProperty(x => x.Estoque, 2));
        original.Estoque = 3;
        try { await stale.SaveChangesAsync(); throw new Exception("Sobrescreveu alteração concorrente"); }
        catch (DbUpdateConcurrencyException) { Check(true, "Edição administrativa detecta alteração concorrente"); }
    }

    // Pedidos antigos não recebem uma baixa retroativa sobre um estoque já ajustado.
    var legacy = NewOrder((variationId, 1));
    setup.Pedidos.Add(legacy); await setup.SaveChangesAsync();
    await Apply(legacy.Id, State(legacy.Id, 10));
    await AssertStock(2, 1, "Pedido legado não desconta estoque retroativamente");
    Console.WriteLine($"{count} verificações aprovadas, incluindo concorrência real em PostgreSQL.");
}
finally
{
    await setup.DisposeAsync();
NpgsqlConnection.ClearAllPools();
await using var drop = new NpgsqlCommand($"DROP DATABASE {schema} WITH (FORCE)", admin);
await drop.ExecuteNonQueryAsync();
}

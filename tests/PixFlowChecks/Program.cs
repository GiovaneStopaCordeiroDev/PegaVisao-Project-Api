using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using PegaVisaoApi.Models;
using PegaVisaoApi.Services;

var checks = 0;
void Check(bool ok, string nome)
{
    if (!ok) throw new Exception(nome);
    Console.WriteLine($"OK: {nome}");
    checks++;
}
JsonElement Order(string status = "action_required", string detail = "waiting_transfer",
    string paymentStatus = "action_required", string amount = "50.00") => JsonSerializer.SerializeToElement(new
    {
        id = "ORD01TEST", external_reference = "22", total_amount = "50.00", status,
        status_detail = detail,
        transactions = new { payments = new[] { new { id = "PAY01TEST", amount,
            status = paymentStatus, status_detail = detail } } }
    });

var pendente = EstadoPagamentoMercadoPago.DaOrder(Order());
Check(!pendente.Confirmado && pendente.OrderId == "ORD01TEST" && pendente.PaymentId == "PAY01TEST",
    "Pix criado preserva IDs separados e não confirma pagamento");
var pago = EstadoPagamentoMercadoPago.DaOrder(Order("processed", "accredited", "processed"));
Check(pago.Confirmado && pago.Status == "processed" && pago.ExternalReference == "22",
    "Orders confirma processed/accredited preservando status original");
Check(!EstadoPagamentoMercadoPago.DaOrder(Order("processed", "accredited")).Confirmado,
    "Order processada com transação pendente não confirma");
foreach (var status in new[] { "failed", "canceled", "refunded", "processing" })
    Check(!EstadoPagamentoMercadoPago.DaOrder(Order(status, status, status)).Confirmado, $"{status} não confirma");
try
{
    EstadoPagamentoMercadoPago.DaOrder(Order(amount: "1.00"));
    throw new Exception("Deveria rejeitar valor divergente");
}
catch (InvalidDataException) { Check(true, "Transação com valor divergente é rejeitada"); }
Check((int)Status.Pendente == 0 && (int)Status.Enviado == 1 && (int)Status.Entregue == 2 &&
    (int)Status.Cancelado == 3 && (int)Status.Pago == 4, "Valores antigos do enum preservados");

var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
{
    ["MercadoPago:AccessToken"] = "fake-token-for-offline-test"
}).Build();
var handler = new FakeHandler(Order());
var service = new MercadoPagoService(new HttpClient(handler), config);
await service.ConsultarOrderAsync("ORD01TEST");
Check(handler.Url == "https://api.mercadopago.com/v1/orders/ORD01TEST", "Consulta ORD usa Orders API");
Check(handler.Bearer == "fake-token-for-offline-test", "Consulta usa token da configuração");
try
{
    await service.ConsultarPagamentoAsync("PAY01TEST");
    throw new Exception("PAY deveria ser rejeitado antes de acessar Payments");
}
catch (ArgumentException) { Check(true, "PAY não é enviado para Payments API"); }
await service.ConsultarPagamentoAsync("123456");
Check(handler.Url!.EndsWith("/v1/payments/123456"), "Payments numérico continua disponível");
handler.Status = HttpStatusCode.ServiceUnavailable;
try
{
    await service.ConsultarOrderAsync("ORD01TEST");
    throw new Exception("Falha do provedor deveria ser propagada");
}
catch (HttpRequestException ex) { Check(ex.StatusCode == HttpStatusCode.ServiceUnavailable, "Falha HTTP preserva código para tratamento"); }
handler.Status = HttpStatusCode.OK;
await service.CriarPixAsync(new Pedido { Id = 22, ValorTotal = 50 }, "cliente@example.com", "Cliente Teste");
using (var body = JsonDocument.Parse(handler.Body!))
    Check(body.RootElement.GetProperty("payer").GetProperty("email").GetString() == "cliente@example.com",
        "Produção usa email do cliente");
config["MercadoPago:PixTeste"] = "true";
await service.CriarPixAsync(new Pedido { Id = 22, ValorTotal = 50 }, "cliente@example.com", "Cliente Teste");
using (var body = JsonDocument.Parse(handler.Body!))
    Check(body.RootElement.GetProperty("payer").GetProperty("first_name").GetString() == "APRO",
        "Simulação APRO exige configuração explícita");
await service.CancelarOrderAsync("ORD01TEST");
Check(handler.Url == "https://api.mercadopago.com/v1/orders/ORD01TEST/cancel"
    && handler.Method == HttpMethod.Post && handler.Idempotency == "cancelar-ORD01TEST",
    "Cancelamento usa endpoint e chave idempotente");
handler.Status = HttpStatusCode.Conflict;
try { await service.CancelarOrderAsync("ORD01TEST"); throw new Exception("Aceitou cancelamento recusado"); }
catch (HttpRequestException e) { Check(e.StatusCode == HttpStatusCode.Conflict, "Cancelamento recusado exige consulta ao provedor"); }
var agora = DateTime.UtcNow;
Check(!PrazoPagamento.DeveCancelar(null, agora, "action_required"), "Pedidos antigos não ganham prazo retroativo");
Check(!PrazoPagamento.DeveCancelar(agora.AddMinutes(1), agora, "action_required"), "Antes do prazo mantém pagamento");
Check(PrazoPagamento.DeveCancelar(agora, agora, "action_required"), "Pix aguardando pagamento pode ser cancelado no prazo");
Check(PrazoPagamento.DeveCancelar(agora, agora, "created"), "Checkout não iniciado pode ser cancelado");
foreach (var status in new[] { "processing", "processed", "in_process", "authorized" })
    Check(!PrazoPagamento.DeveCancelar(agora.AddMinutes(-1), agora, status), $"Prazo preserva pagamento {status}");
handler.Status = HttpStatusCode.OK;
await service.CriarPixAsync(new Pedido { Id = 22, ValorTotal = 50 }, "cliente@example.com", "Cliente");
using (var body = JsonDocument.Parse(handler.Body!))
    Check(body.RootElement.GetProperty("transactions").GetProperty("payments")[0]
        .GetProperty("expiration_time").GetString() == "PT30M", "Pix respeita mínimo de 30 minutos do provedor");
await service.CriarOrderAsync(new Pedido { Id = 23, ValorTotal = 50,
    Itens = new List<ItemPedido> { new() { Quantidade = 1, PrecoUnitario = 50,
        VariacaoProduto = new() { Produto = new() { Nome = "Teste" } } } } });
using (var body = JsonDocument.Parse(handler.Body!))
    Check(body.RootElement.GetProperty("expiration_time").GetString() == "PT15M", "Checkout Pro recebe validade de 15 minutos");
Check(ParcelamentoProduto.Calcular(120).Quantidade == 12 && ParcelamentoProduto.Calcular(120).ValorParcela == 10,
    "Produto de 120 anuncia 12 parcelas de 10");
Check(ParcelamentoProduto.Calcular(25).Quantidade == 5, "Quantidade respeita mínimo da loja de 5 reais");
Check(ParcelamentoProduto.Calcular(1).Quantidade == 1, "Produto de teste de 1 real não anuncia parcelamento");
Check(ParcelamentoProduto.Calcular(199.90m).ValorParcela == 16.66m, "Simulação arredonda centavos");
using (var body = JsonDocument.Parse(handler.Body!))
{
    var pagamento = body.RootElement.GetProperty("config").GetProperty("payment_method");
    Check(pagamento.GetProperty("max_installments").GetInt32() == 10, "Checkout usa total de 50 para máximo de parcelas");
    Check(pagamento.GetProperty("installments_cost").GetString() == "seller"
        && pagamento.GetProperty("installments").GetProperty("interest_free").GetProperty("values")[1].GetInt32() == 10,
        "Checkout alinha parcelas sem juros à simulação");
}
Console.WriteLine($"{checks} verificações passaram; HTTP simulado, sem acesso ao banco ou Mercado Pago.");

sealed class FakeHandler(JsonElement response) : HttpMessageHandler
{
    public string? Url, Bearer, Body, Idempotency; public HttpMethod? Method;
    public HttpStatusCode Status = HttpStatusCode.OK;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Url = request.RequestUri!.ToString();
        Method = request.Method;
        Idempotency = request.Headers.TryGetValues("X-Idempotency-Key", out var keys) ? keys.Single() : null;
        Bearer = request.Headers.Authorization?.Parameter;
        Body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        return new HttpResponseMessage(Status) { Content = new StringContent(response.GetRawText(), Encoding.UTF8, "application/json") };
    }
}

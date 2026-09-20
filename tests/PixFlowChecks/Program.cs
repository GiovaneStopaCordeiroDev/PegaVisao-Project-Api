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
Console.WriteLine($"{checks} verificações passaram; HTTP simulado, sem acesso ao banco ou Mercado Pago.");

sealed class FakeHandler(JsonElement response) : HttpMessageHandler
{
    public string? Url, Bearer, Body;
    public HttpStatusCode Status = HttpStatusCode.OK;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Url = request.RequestUri!.ToString();
        Bearer = request.Headers.Authorization?.Parameter;
        Body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        return new HttpResponseMessage(Status) { Content = new StringContent(response.GetRawText(), Encoding.UTF8, "application/json") };
    }
}

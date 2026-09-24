using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using PegaVisaoApi.Models;
namespace PegaVisaoApi.Services
{
    public class MercadoPagoService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<MercadoPagoService> _logger;
        public MercadoPagoService(HttpClient httpClient, IConfiguration configuration, ILogger<MercadoPagoService>? logger = null)
        {
            _httpClient = httpClient; _configuration = configuration;
            _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<MercadoPagoService>.Instance;
        }
        public string MontarPayload(Pedido pedido, string email = "", string nome = "") =>
            PayloadPagamento.Criar(pedido, _configuration.GetValue<bool>("MercadoPago:PixTeste"), email, nome);
        public Task<JsonElement> CriarOrderAsync(Pedido pedido) => EnviarCriacaoAsync(MontarPayload(pedido), pedido.PaymentIdempotencyKey ?? $"pedido-{pedido.Id}");
        public Task<JsonElement> CriarPixAsync(Pedido pedido, string emailCliente, string nomeCliente) =>
            EnviarCriacaoAsync(MontarPayload(pedido, emailCliente, nomeCliente), pedido.PaymentIdempotencyKey ?? $"pix-pedido-{pedido.Id}");
        public async Task<JsonElement> EnviarCriacaoAsync(string payload, string chave)
        {
            var token = _configuration["MercadoPago:AccessToken"];
            if (string.IsNullOrWhiteSpace(token)) throw new MercadoPagoException(503, "credencial_ausente", "Pagamento indisponível: configuração do provedor ausente.");
            using var structure = JsonDocument.Parse(payload);
            _logger.LogWarning(
                "BACKEND MP VERSAO SEM INSTALLMENTS_COST"
            );

            if (structure.RootElement.TryGetProperty("config", out var config))
            {
                _logger.LogWarning(
                    "CONFIG REAL ENVIADA AO MERCADO PAGO: {Config}",
                    config.GetRawText()
                );
            }

            _logger.LogInformation(
                "MP POST /v1/orders: campos {Campos}, modo {Modo}, idempotência persistida",
                string.Join(
                    ",",
                    structure.RootElement.EnumerateObject().Select(p => p.Name)
                ),
                structure.RootElement.TryGetProperty(
                    "processing_mode",
                    out var mode
                )
                    ? mode.GetString()
                    : "automatic"
            );
            _logger.LogInformation("MP POST /v1/orders: campos {Campos}, modo {Modo}, idempotência persistida",
                string.Join(",", structure.RootElement.EnumerateObject().Select(p => p.Name)),
                structure.RootElement.TryGetProperty("processing_mode", out var mode) ? mode.GetString() : "automatic");
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.mercadopago.com/v1/orders");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("X-Idempotency-Key", chave);
            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            try {
                using var response = await _httpClient.SendAsync(request, timeout.Token);
                var resposta = await response.Content.ReadAsStringAsync(timeout.Token);
                if (!response.IsSuccessStatusCode) {
                    var status = (int)response.StatusCode;
                    _logger.LogWarning("MP criação recusada: HTTP {Status}", status);
                    var mapped = status >= 500 ? 502 : status == 423 ? 409 : status;
                    throw new MercadoPagoException(mapped, $"mp_http_{status}", status switch {
                        400 => "O provedor recusou os dados do pagamento. O pedido foi preservado.",
                        401 or 403 => "O provedor recusou as credenciais de pagamento. A loja precisa revisar a configuração.",
                        409 or 423 => "Pagamento em processamento ou conflito de idempotência. Consulte o mesmo pedido antes de tentar novamente.",
                        422 => "O pagamento não atende às regras do provedor.",
                        429 => "Muitas tentativas de pagamento. Aguarde antes de tentar novamente.",
                        _ => "Não foi possível comunicar com o provedor de pagamento. Seu pedido foi preservado."
                    });
                }
                using var doc = JsonDocument.Parse(resposta);
                return doc.RootElement.Clone();
            } catch (OperationCanceledException) { throw new MercadoPagoException(504, "mp_timeout", "O provedor demorou a responder. Tente novamente neste mesmo pedido."); }
            catch (HttpRequestException) { throw new MercadoPagoException(502, "mp_conexao", "Falha de comunicação com o provedor. Tente novamente neste mesmo pedido."); }
            catch (JsonException) { throw new MercadoPagoException(502, "mp_resposta_invalida", "O provedor retornou uma resposta inválida. O pedido foi preservado."); }
        }
        public async Task CancelarOrderAsync(string orderId)
        {
            if (!orderId.StartsWith("ORD", StringComparison.Ordinal) || !orderId.All(char.IsAsciiLetterOrDigit))
                throw new ArgumentException("Order ID inválido.");
            using var request = new HttpRequestMessage(HttpMethod.Post,
                $"https://api.mercadopago.com/v1/orders/{orderId}/cancel");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer",
                _configuration["MercadoPago:AccessToken"]);
            request.Headers.Add("X-Idempotency-Key", $"cancelar-{orderId}");
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var response = await _httpClient.SendAsync(request, timeout.Token);
            response.EnsureSuccessStatusCode();
        }
        public async Task<JsonElement> ConsultarPagamentoAsync(string paymentId)
        {
            if (string.IsNullOrWhiteSpace(paymentId) || !paymentId.All(char.IsAsciiDigit))
                throw new ArgumentException("Payments API exige um ID numérico. Para PAY..., consulte a order ORD... correspondente.");
            return await ConsultarRecursoAsync("payments", paymentId);
        }

        public Task<JsonElement> ConsultarOrderAsync(string orderId)
        {
            if (string.IsNullOrWhiteSpace(orderId) || !orderId.StartsWith("ORD", StringComparison.Ordinal) || !orderId.All(char.IsAsciiLetterOrDigit))
                throw new ArgumentException("Order ID inválido; esperado ORD...");
            return ConsultarRecursoAsync("orders", orderId);
        }

        private async Task<JsonElement> ConsultarRecursoAsync(string recurso, string paymentId)
        {
            var accessToken =
                _configuration["MercadoPago:AccessToken"];

            if (string.IsNullOrWhiteSpace(accessToken))
            {
                throw new Exception(
                    "Access Token do Mercado Pago não configurado."
                );
            }

            if (string.IsNullOrWhiteSpace(paymentId))
            {
                throw new Exception(
                    "Payment ID não informado."
                );
            }

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"https://api.mercadopago.com/v1/{recurso}/{Uri.EscapeDataString(paymentId)}"
            );

            request.Headers.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    accessToken
                );

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var response = await _httpClient.SendAsync(request, timeout.Token);

            var resposta =
                await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException("Falha na consulta ao Mercado Pago.", null, response.StatusCode);
            }

            using var document =
                JsonDocument.Parse(resposta);

            return document.RootElement.Clone();
        }

    }


}

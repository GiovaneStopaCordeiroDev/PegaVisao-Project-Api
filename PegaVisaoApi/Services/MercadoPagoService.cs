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

        public MercadoPagoService(
            HttpClient httpClient,
            IConfiguration configuration)
        {
            _httpClient = httpClient;
            _configuration = configuration;
        }

        // ============================================================
        // CARTÃO
        // ============================================================

        public async Task<JsonElement> CriarOrderAsync(Pedido pedido)
        {
            var accessToken =
                _configuration["MercadoPago:AccessToken"];

            if (string.IsNullOrWhiteSpace(accessToken))
            {
                throw new Exception(
                    "Access Token do Mercado Pago não configurado."
                );
            }

            if (pedido.Itens == null || pedido.Itens.Count == 0)
            {
                throw new Exception(
                    "O pedido não possui itens."
                );
            }

            var itens = pedido.Itens.Select(item =>
            {
                if (item.VariacaoProduto?.Produto == null)
                {
                    throw new Exception(
                        $"Produto do item {item.Id} não encontrado."
                    );
                }

                return new
                {
                    title = item.VariacaoProduto.Produto.Nome,

                    unit_price = item.PrecoUnitario.ToString(
                        System.Globalization.CultureInfo.InvariantCulture
                    ),

                    quantity = item.Quantidade
                };
            }).ToList();
            if (pedido.ValorFrete > 0)
                itens.Add(new
                {
                    title = "Frete - " + pedido.FreteServico,
                    unit_price = pedido.ValorFrete.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    quantity = 1
                });

            var valorTotal = pedido.ValorTotal.ToString(
                System.Globalization.CultureInfo.InvariantCulture
            );

            var body = new
            {
                type = "online",

                processing_mode = "manual",
                expiration_time = "PT15M",

                total_amount = valorTotal,

                external_reference = pedido.Id.ToString(),

                items = itens,
                config = new { payment_method = ParcelamentoProduto.ConfigurarCheckout(pedido.ValorTotal) }
            };

            var json = JsonSerializer.Serialize(body);

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                "https://api.mercadopago.com/v1/orders"
            );

            request.Headers.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    accessToken
                );

            request.Headers.Add(
                "X-Idempotency-Key",
                $"pedido-{pedido.Id}"
            );

            request.Content = new StringContent(
                json,
                Encoding.UTF8,
                "application/json"
            );

            var response =
                await _httpClient.SendAsync(request);

            var resposta =
                await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception(
                    $"Erro Mercado Pago: {resposta}"
                );
            }

            using var document =
                JsonDocument.Parse(resposta);

            return document.RootElement.Clone();
        }

        // ============================================================
        // PIX
        // ============================================================

        public async Task<JsonElement> CriarPixAsync(
            Pedido pedido,
            string emailCliente,
            string nomeCliente)
        {
            var accessToken =
                _configuration["MercadoPago:AccessToken"];

            if (string.IsNullOrWhiteSpace(accessToken))
            {
                throw new Exception(
                    "Access Token do Mercado Pago não configurado."
                );
            }

            if (string.IsNullOrWhiteSpace(emailCliente))
            {
                throw new Exception(
                    "E-mail do cliente não informado."
                );
            }

            if (pedido.ValorTotal <= 0)
            {
                throw new Exception(
                    "O valor do pedido deve ser maior que zero."
                );
            }

            var valorTotal = pedido.ValorTotal.ToString(
                System.Globalization.CultureInfo.InvariantCulture
            );

            var testePix = _configuration.GetValue<bool>("MercadoPago:PixTeste");
            var body = new
            {
                type = "online",

                total_amount = valorTotal,

                external_reference = pedido.Id.ToString(),

                payer = new
                {
                    email = testePix ? "test_user_br@testuser.com" : emailCliente,
                    first_name = testePix ? "APRO" : nomeCliente.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
                },

                transactions = new
                        {
                            payments = new[]
                {
                    new
                    {
                        amount = valorTotal,
                        expiration_time = "PT30M",

                        payment_method = new
                        {
                            id = "pix",
                            type = "bank_transfer"
                        }
                    }
                }
                        }
                    };

            var json = JsonSerializer.Serialize(body);

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                "https://api.mercadopago.com/v1/orders"
            );

            request.Headers.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    accessToken
                );

            request.Headers.Add(
                "X-Idempotency-Key",
                $"pix-pedido-{pedido.Id}"
            );

            request.Content = new StringContent(
                json,
                Encoding.UTF8,
                "application/json"
            );

            var response =
                await _httpClient.SendAsync(request);

            var resposta =
                await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception(
                    $"Erro Mercado Pago Pix: {resposta}"
                );
            }

            using var document =
                JsonDocument.Parse(resposta);

            return document.RootElement.Clone();
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

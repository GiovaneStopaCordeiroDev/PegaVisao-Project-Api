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
                _configuration["MercadoPago:APP_USR-8545403186381729-092009-8288b7d1f1db32f991f630ab2e741da3-1200567027"];

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
            }).ToArray();

            var valorTotal = pedido.ValorTotal.ToString(
                System.Globalization.CultureInfo.InvariantCulture
            );

            var body = new
            {
                type = "online",

                processing_mode = "manual",

                total_amount = valorTotal,

                external_reference = pedido.Id.ToString(),

                items = itens
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
            string emailCliente)
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

            var body = new
            {
                type = "online",

                total_amount = "50.00",

                external_reference = pedido.Id.ToString(),

                payer = new
                {
                    email = "test_user_br@testuser.com",
                    first_name = "APRO"
                },

                transactions = new
                        {
                            payments = new[]
                {
                    new
                    {
                        amount = "50.00",

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
    }
}
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
    }
}
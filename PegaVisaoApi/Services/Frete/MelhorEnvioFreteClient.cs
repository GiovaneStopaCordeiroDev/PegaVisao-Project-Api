using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PegaVisaoApi.Services.MelhorEnvio;

namespace PegaVisaoApi.Services.Frete;

public sealed class MelhorEnvioFreteClient(HttpClient http, IOptions<MelhorEnvioOptions> options)
{
    public async Task<List<FreteOpcao>> CotarAsync(string token, string cepOrigem, string cepDestino,
        IReadOnlyCollection<FreteItem> itens, CancellationToken ct)
    {
        var config = options.Value;
        config.Validar();
        using var request = new HttpRequestMessage(HttpMethod.Post, config.BaseUrl + "/api/v2/me/shipment/calculate");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.UserAgent.ParseAdd($"PegaVisao/1.0 ({config.EmailContato})");
        request.Content = JsonContent.Create(new
        {
            from = new { postal_code = cepOrigem }, to = new { postal_code = cepDestino },
            products = itens.Select(i => new
            {
                id = i.VariacaoId.ToString(), width = i.LarguraCm, height = i.AlturaCm,
                length = i.ComprimentoCm, weight = i.PesoKg, insurance_value = i.Preco, quantity = i.Quantidade
            }),
            options = new { receipt = false, own_hand = false }
        });
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new FreteException(503, "Não foi possível consultar as transportadoras. Tente novamente em instantes ou entre em contato com a loja.");
        try
        {
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            return FreteRegras.LerOpcoes(json.RootElement);
        }
        catch (JsonException) { throw new FreteException(502, "Resposta inválida do serviço de frete."); }
    }
}

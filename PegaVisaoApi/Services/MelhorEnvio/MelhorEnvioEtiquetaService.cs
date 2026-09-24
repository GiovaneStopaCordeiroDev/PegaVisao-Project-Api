using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PegaVisaoApi.Data;
using PegaVisaoApi.Models;
using PegaVisaoApi.Services.Frete;

namespace PegaVisaoApi.Services.MelhorEnvio;

public sealed record GerarEtiquetaAdminRequest(string Documento, string Telefone, string? ChaveNfe);

public sealed class MelhorEnvioEtiquetaService(
    PegaVisaoContext db,
    MelhorEnvioService conexao,
    HttpClient http,
    IOptions<MelhorEnvioOptions> options)
{
    private readonly MelhorEnvioOptions _options = options.Value;

    public async Task<object> GerarAsync(int pedidoId, GerarEtiquetaAdminRequest dados, CancellationToken ct)
    {
        _options.Validar();
        _options.ValidarRemetente();

        var pedido = await db.Pedidos
            .Include(p => p.Usuario)
            .Include(p => p.Itens)
                .ThenInclude(i => i.VariacaoProduto)
                    .ThenInclude(v => v.Produto)
            .SingleOrDefaultAsync(p => p.Id == pedidoId, ct)
            ?? throw new MelhorEnvioException(404, "Pedido não encontrado.");

        if (pedido.Status is not (Status.Pago or Status.Enviado))
            throw new MelhorEnvioException(409, "A etiqueta só pode ser gerada após a confirmação do pagamento.");

        if (pedido.FreteServicoId is null or <= 0 || pedido.FreteServicoId == int.MaxValue)
            throw new MelhorEnvioException(409, "Este pedido não possui um frete válido para geração de etiqueta.");

        var documento = Digitos(dados.Documento);
        var telefone = Digitos(dados.Telefone);
        var nfe = Digitos(dados.ChaveNfe);

        if (documento.Length != 11)
            throw new MelhorEnvioException(400, "Informe o CPF do destinatário com 11 dígitos.");
        if (telefone.Length is < 10 or > 11)
            throw new MelhorEnvioException(400, "Informe um telefone válido do destinatário.");
        if (nfe.Length != 0 && nfe.Length != 44)
            throw new MelhorEnvioException(400, "A chave da NF-e deve possuir 44 dígitos.");

        if (nfe.Length == 0 && !_options.PermitirDeclaracaoConteudo)
            throw new MelhorEnvioException(422,
                "Informe a chave da NF-e. Se a loja puder usar Declaração de Conteúdo neste envio, habilite MelhorEnvio__PermitirDeclaracaoConteudo=true.");

        var token = await conexao.ObterAccessTokenAsync(ct);

        if (string.IsNullOrWhiteSpace(pedido.MelhorEnvioOrderId))
        {
            var volumes = LerVolumes(pedido);
            if (pedido.FreteServicoId is 1 or 2 or 17 && volumes.Count > 1)
                throw new MelhorEnvioException(422,
                    "A cotação deste pedido possui múltiplos volumes. Para Correios, cada volume precisa de uma etiqueta separada.");

            var payload = MontarCarrinho(pedido, documento, telefone, nfe, volumes);
            var cart = await EnviarJsonAsync(HttpMethod.Post, "/api/v2/me/cart", token, payload, ct);

            var orderId = cart.TryGetProperty("id", out var id) ? id.GetString() : null;
            if (string.IsNullOrWhiteSpace(orderId))
                throw new MelhorEnvioException(502, "O Melhor Envio não retornou o identificador da etiqueta.");

            pedido.MelhorEnvioOrderId = orderId;
            pedido.MelhorEnvioEtiquetaStatus = "carrinho";
            await db.SaveChangesAsync(ct);
        }

        var ids = new[] { pedido.MelhorEnvioOrderId! };

        if (pedido.MelhorEnvioEtiquetaStatus == "carrinho")
        {
            await EnviarJsonAsync(HttpMethod.Post, "/api/v2/me/shipment/checkout", token, new { orders = ids }, ct);
            pedido.MelhorEnvioEtiquetaStatus = "comprada";
            await db.SaveChangesAsync(ct);
        }

        if (pedido.MelhorEnvioEtiquetaStatus != "gerada")
        {
            await EnviarJsonAsync(HttpMethod.Post, "/api/v2/me/shipment/generate", token, new { orders = ids }, ct);
            pedido.MelhorEnvioEtiquetaStatus = "gerada";
            pedido.MelhorEnvioEtiquetaGeradaEm = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        return new
        {
            pedido.Id,
            pedido.MelhorEnvioOrderId,
            statusEtiqueta = pedido.MelhorEnvioEtiquetaStatus,
            pedido.MelhorEnvioEtiquetaGeradaEm
        };
    }

    public async Task<string> ObterLinkImpressaoAsync(int pedidoId, CancellationToken ct)
    {
        _options.Validar();
        var pedido = await db.Pedidos.AsNoTracking().SingleOrDefaultAsync(p => p.Id == pedidoId, ct)
            ?? throw new MelhorEnvioException(404, "Pedido não encontrado.");

        if (pedido.MelhorEnvioEtiquetaStatus != "gerada" || string.IsNullOrWhiteSpace(pedido.MelhorEnvioOrderId))
            throw new MelhorEnvioException(409, "Gere a etiqueta antes de solicitar a impressão.");

        var token = await conexao.ObterAccessTokenAsync(ct);
        var json = await EnviarJsonAsync(HttpMethod.Post, "/api/v2/me/shipment/print", token,
            new { mode = "public", orders = new[] { pedido.MelhorEnvioOrderId } }, ct);

        if (json.ValueKind == JsonValueKind.Object &&
            json.TryGetProperty("url", out var url) &&
            Uri.TryCreate(url.GetString(), UriKind.Absolute, out var uri) &&
            uri.Scheme is "https" or "http")
            return uri.AbsoluteUri;

        if (json.ValueKind == JsonValueKind.String &&
            Uri.TryCreate(json.GetString(), UriKind.Absolute, out var direct) &&
            direct.Scheme is "https" or "http")
            return direct.AbsoluteUri;

        throw new MelhorEnvioException(502, "O Melhor Envio não retornou o link de impressão da etiqueta.");
    }

    private object MontarCarrinho(Pedido pedido, string documento, string telefone, string nfe,
        IReadOnlyCollection<object> volumes)
    {
        var remetenteDocumento = Digitos(_options.RemetenteDocumento);

        var remetente = new Dictionary<string, object?>
        {
            ["name"] = _options.RemetenteNome,
            ["email"] = _options.RemetenteEmail,
            ["phone"] = Digitos(_options.RemetenteTelefone),
            ["address"] = _options.RemetenteEndereco,
            ["complement"] = _options.RemetenteComplemento,
            ["number"] = _options.RemetenteNumero,
            ["district"] = _options.RemetenteBairro,
            ["city"] = _options.RemetenteCidade,
            ["country_id"] = "BR",
            ["postal_code"] = FreteRegras.NormalizarCep(_options.CepOrigem),
            ["state_abbr"] = _options.RemetenteEstado.Trim().ToUpperInvariant()
        };

        if (remetenteDocumento.Length == 11)
            remetente["document"] = remetenteDocumento;
        else
        {
            remetente["company_document"] = remetenteDocumento;
            remetente["state_register"] = string.IsNullOrWhiteSpace(_options.RemetenteInscricaoEstadual)
                ? "ISENTO" : _options.RemetenteInscricaoEstadual.Trim();
        }

        var destinatario = new
        {
            name = pedido.Usuario.Nome,
            phone = telefone,
            email = pedido.Usuario.Email,
            document = documento,
            address = pedido.Rua,
            complement = pedido.Complemento ?? "",
            number = pedido.Numero,
            district = pedido.Bairro,
            city = pedido.Cidade,
            country_id = "BR",
            postal_code = FreteRegras.NormalizarCep(pedido.Cep),
            state_abbr = pedido.Estado.Trim().ToUpperInvariant()
        };

        var opcoes = new Dictionary<string, object?>
        {
            ["insurance_value"] = decimal.Round(Math.Max(0, pedido.SubtotalProdutos), 2),
            ["receipt"] = false,
            ["own_hand"] = false,
            ["reverse"] = false,
            ["non_commercial"] = nfe.Length == 0,
            ["platform"] = "PegaVisao",
            ["reminder"] = $"Pedido #{pedido.Id}"
        };
        if (nfe.Length == 44)
            opcoes["invoice"] = new { key = nfe };

        return new
        {
            service = pedido.FreteServicoId,
            from = remetente,
            to = destinatario,
            products = pedido.Itens.Select(i => new
            {
                name = i.VariacaoProduto.Produto.Nome,
                quantity = i.Quantidade,
                unitary_value = decimal.Round(i.PrecoUnitario, 2)
            }).ToArray(),
            volumes,
            options = opcoes
        };
    }

    private static List<object> LerVolumes(Pedido pedido)
    {
        if (string.IsNullOrWhiteSpace(pedido.FreteVolumesJson))
            throw new MelhorEnvioException(422, "O pedido não possui os volumes retornados pela cotação de frete.");

        try
        {
            using var doc = JsonDocument.Parse(pedido.FreteVolumesJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                throw new JsonException();

            var result = new List<object>();
            foreach (var pacote in doc.RootElement.EnumerateArray())
            {
                var dimensoes = pacote.GetProperty("dimensions");
                result.Add(new
                {
                    height = Numero(dimensoes.GetProperty("height")),
                    width = Numero(dimensoes.GetProperty("width")),
                    length = Numero(dimensoes.GetProperty("length")),
                    weight = Numero(pacote.GetProperty("weight"))
                });
            }

            if (result.Count == 0) throw new JsonException();
            return result;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            throw new MelhorEnvioException(422, "Os volumes salvos na cotação de frete são inválidos para gerar a etiqueta.");
        }
    }

    private async Task<JsonElement> EnviarJsonAsync(HttpMethod method, string path, string token, object body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, _options.BaseUrl + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.UserAgent.ParseAdd($"PegaVisao/1.0 ({_options.EmailContato})");
        request.Content = JsonContent.Create(body);

        using var response = await http.SendAsync(request, ct);
        var texto = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new MelhorEnvioException(response.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity ? 422 : 502,
                $"O Melhor Envio recusou a operação de etiqueta (HTTP {(int)response.StatusCode}).");

        if (string.IsNullOrWhiteSpace(texto)) return JsonDocument.Parse("{}").RootElement.Clone();
        try
        {
            using var json = JsonDocument.Parse(texto);
            return json.RootElement.Clone();
        }
        catch (JsonException)
        {
            throw new MelhorEnvioException(502, "O Melhor Envio retornou uma resposta inválida.");
        }
    }

    private static string Digitos(string? valor) => new((valor ?? "").Where(char.IsDigit).ToArray());

    private static decimal Numero(JsonElement valor)
    {
        if (valor.ValueKind == JsonValueKind.Number && valor.TryGetDecimal(out var numero)) return numero;
        if (valor.ValueKind == JsonValueKind.String &&
            decimal.TryParse(valor.GetString(), System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out numero)) return numero;
        throw new FormatException();
    }
}

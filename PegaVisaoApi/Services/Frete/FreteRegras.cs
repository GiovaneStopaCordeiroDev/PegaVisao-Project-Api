using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PegaVisaoApi.Models;
using PegaVisaoApi.DTO_s;

namespace PegaVisaoApi.Services.Frete;

public sealed class FreteException(int status, string mensagem) : Exception(mensagem)
{
    public int Status { get; } = status;
}
public record FreteItem(int VariacaoId, int Quantidade, decimal Preco, decimal PesoKg,
    decimal AlturaCm, decimal LarguraCm, decimal ComprimentoCm);
public record FreteOpcao(int ServicoId, string Servico, string Transportadora, decimal Valor,
    int PrazoDias, string? VolumesJson);

public static class FreteRegras
{
    public static bool CpfValido(string? cpf)
    {
        var numeros = new string((cpf ?? "").Where(char.IsDigit).ToArray());
        if (numeros.Length != 11 || numeros.Distinct().Count() == 1) return false;

        static int Digito(string valor, int tamanho)
        {
            var soma = 0;
            for (var i = 0; i < tamanho; i++)
                soma += (valor[i] - '0') * (tamanho + 1 - i);
            var resto = soma % 11;
            return resto < 2 ? 0 : 11 - resto;
        }

        return Digito(numeros, 9) == numeros[9] - '0' &&
               Digito(numeros, 10) == numeros[10] - '0';
    }

    public static string NormalizarCep(string? cep)
    {
        if (cep == null || !Regex.IsMatch(cep.Trim(), @"^\d{5}-?\d{3}$", RegexOptions.ECMAScript))
            throw new FreteException(400, "Informe um CEP válido com oito dígitos.");
        return cep.Trim().Replace("-", "");
    }

    public static Dictionary<int, int> AgruparItens(IReadOnlyCollection<CreateItemPedidoDto>? itens)
    {
        if (itens == null || itens.Count == 0 || itens.Count > 50 ||
            itens.Any(i => i.VariacaoProdutoId <= 0 || i.Quantidade <= 0 || i.Quantidade > 100))
            throw new FreteException(400, "Carrinho inválido. Use de 1 a 50 itens, com até 100 unidades por variação.");
        var result = itens.GroupBy(i => i.VariacaoProdutoId).ToDictionary(g => g.Key, g => g.Sum(i => i.Quantidade));
        if (result.Values.Any(q => q > 100) || result.Values.Sum() > 500)
            throw new FreteException(400, "Quantidade de itens acima do limite do checkout.");
        return result;
    }

    public static FreteItem PrepararItem(VariacaoProduto variacao, int quantidade, bool exigirMedidas = true)
    {
        if (quantidade <= 0 || quantidade > variacao.EstoqueDisponivel)
            throw new FreteException(409, "Quantidade indisponível em estoque. Atualize o carrinho.");
        var p = variacao.Produto;
        if (p == null || p.Preco <= 0 || decimal.Round(p.Preco, 2) != p.Preco)
            throw new FreteException(422, "Um produto está com preço inválido. Entre em contato com a loja.");
        if (!exigirMedidas)
            return new(variacao.Id, quantidade, p.Preco, 0, 0, 0, 0);
        if (p == null || p.PesoKg is not (> 0 and <= 1000) || p.AlturaCm is not (> 0 and <= 1000) ||
            p.LarguraCm is not (> 0 and <= 1000) || p.ComprimentoCm is not (> 0 and <= 1000))
            throw new FreteException(422, $"O produto {p?.Nome ?? variacao.Id.ToString()} ainda não possui peso e dimensões válidos para entrega. Entre em contato com a loja.");
        if (p.Preco <= 0 || decimal.Round(p.Preco, 2) != p.Preco)
            throw new FreteException(422, "Um produto está com preço inválido. Entre em contato com a loja.");
        return new(variacao.Id, quantidade, p.Preco, p.PesoKg.Value, p.AlturaCm.Value, p.LarguraCm.Value, p.ComprimentoCm.Value);
    }

    public static string HashCarrinho(IEnumerable<FreteItem> itens, string cepOrigem)
    {
        string N(decimal n) => n.ToString("G29", CultureInfo.InvariantCulture);
        var texto = cepOrigem + "|" + string.Join(";", itens.OrderBy(i => i.VariacaoId).Select(i =>
            $"{i.VariacaoId}:{i.Quantidade}:{N(i.Preco)}:{N(i.PesoKg)}:{N(i.AlturaCm)}:{N(i.LarguraCm)}:{N(i.ComprimentoCm)}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(texto)));
    }

    public static void ValidarCotacao(CotacaoFrete cotacao, int usuarioId, string cep, string hash,
        string conexaoId, DateTime agora)
    {
        if (cotacao.UsuarioId != usuarioId) throw new FreteException(404, "Cotação não encontrada.");
        if (cotacao.ConsumidaEm != null) throw new FreteException(409, "Esta cotação já foi usada em um pedido. Confira Meus pedidos antes de tentar novamente.");
        if (cotacao.ExpiraEm <= agora || cotacao.CepDestino != cep || cotacao.CarrinhoHash != hash || cotacao.ConexaoId != conexaoId)
            throw new FreteException(409, "A cotação venceu ou os dados da compra mudaram. Volte ao checkout e calcule o frete novamente.");
    }

    public static List<FreteOpcao> LerOpcoes(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array) throw new FreteException(502, "Resposta inválida do serviço de frete.");
        var opcoes = new List<FreteOpcao>();
        foreach (var item in root.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object ||
                (item.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null && error.ToString() != "")) continue;
            if (!item.TryGetProperty("id", out var id) || !int.TryParse(id.ToString(), out var servicoId) || servicoId <= 0 ||
                !item.TryGetProperty("custom_price", out var price) ||
                !decimal.TryParse(price.ToString(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var valor) || valor < 0 ||
                !item.TryGetProperty("custom_delivery_time", out var days) || !int.TryParse(days.ToString(), out var prazo) || prazo < 0 ||
                !item.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(name.GetString()) ||
                !item.TryGetProperty("company", out var company) || company.ValueKind != JsonValueKind.Object ||
                !company.TryGetProperty("name", out var companyName) || companyName.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(companyName.GetString())) continue;
            opcoes.Add(new(servicoId, name.GetString()!, companyName.GetString()!, decimal.Round(valor, 2, MidpointRounding.AwayFromZero),
                prazo, item.TryGetProperty("packages", out var packages) ? packages.GetRawText() : null));
        }
        return opcoes.DistinctBy(o => o.ServicoId).OrderBy(o => o.Valor).ToList();
    }
}

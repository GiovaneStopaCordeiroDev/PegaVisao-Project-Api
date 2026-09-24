using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using PegaVisaoApi.Models;
namespace PegaVisaoApi.Services;
public static class PayloadPagamento
{
    private static string Moeda(decimal valor) => valor.ToString("0.00", CultureInfo.InvariantCulture);
    public static string Criar(Pedido pedido, bool teste, string email = "", string nome = "")
    {
        if (pedido.ValorTotal <= 0 || pedido.Itens.Count == 0 ||
            pedido.Itens.Sum(i => i.PrecoUnitario * i.Quantidade) - pedido.ValorDesconto + pedido.ValorFrete != pedido.ValorTotal)
            throw new MercadoPagoException(422, "total_invalido", "O total do pedido está inconsistente.");
        object body;
        if (pedido.FormaPagamento == "Pix") {
            if (string.IsNullOrWhiteSpace(email)) throw new MercadoPagoException(422, "email_ausente", "Informe um e-mail válido na sua conta.");
            body = new {
                type = "online", total_amount = Moeda(pedido.ValorTotal), external_reference = pedido.Id.ToString(),
                payer = new { email = teste ? "test_user_br@testuser.com" : email,
                    first_name = teste ? "APRO" : nome.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() },
                transactions = new { payments = new[] { new {
                    amount = Moeda(pedido.ValorTotal), expiration_time = "PT15M",
                    payment_method = new { id = "pix", type = "bank_transfer" }
                } } }
            };
        } else {
            // Distribui o desconto em centavos. Linhas agregadas evitam arredondar por unidade.
            var itens = new List<object>();
            var restante = pedido.ValorDesconto;
            var ordenados = pedido.Itens.OrderBy(i => i.VariacaoProdutoId).ToList();
            var subtotal = ordenados.Sum(i => i.PrecoUnitario * i.Quantidade);
            for (int n = 0; n < ordenados.Count; n++) {
                var item = ordenados[n];
                var bruto = item.PrecoUnitario * item.Quantidade;
                var desconto = n == ordenados.Count - 1 ? restante : decimal.Floor(pedido.ValorDesconto * bruto / subtotal * 100m) / 100m;
                restante -= desconto;
                if (bruto - desconto > 0) itens.Add(new { title = $"{item.VariacaoProduto.Produto.Nome} ({item.Quantidade} un.)", unit_price = Moeda(bruto - desconto), quantity = 1 });
            }
            if (pedido.ValorFrete > 0) itens.Add(new { title = "Frete", unit_price = Moeda(pedido.ValorFrete), quantity = 1 });
            body = new {
                type = "online", processing_mode = "manual", expiration_time = "PT15M",
                total_amount = Moeda(pedido.ValorTotal), external_reference = pedido.Id.ToString(), items = itens,
                config = new { payment_method = ParcelamentoProduto.ConfigurarCheckout(pedido.ValorTotal) }
            };
        }
        return JsonSerializer.Serialize(body, new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });
    }
}

namespace PegaVisaoApi.Services;

// Política da loja: até 12 parcelas sem juros, com mínimo de R$ 5 por parcela.
// A disponibilidade final depende do cartão e da aprovação do Mercado Pago.
public static class ParcelamentoProduto
{
    public const int MaximoParcelas = 12;
    public const decimal ParcelaMinima = 5m;

    public static SimulacaoParcelamento Calcular(decimal preco)
    {
        if (preco <= 0) return new(1, 0);
        var quantidade = (int)Math.Max(1m, Math.Min(MaximoParcelas, decimal.Floor(preco / ParcelaMinima)));
        return new(quantidade, decimal.Round(preco / quantidade, 2, MidpointRounding.AwayFromZero));
    }

    public static object ConfigurarCheckout(decimal total)
    {
        var quantidade = Calcular(total).Quantidade;
        if (quantidade < 2) return new { max_installments = 1 };
        return new
        {
            max_installments = quantidade,
            installments_cost = "seller",
            installments = new
            {
                interest_free = new { type = "range", values = new[] { 2, quantidade } }
            }
        };
    }
}

public record SimulacaoParcelamento(int Quantidade, decimal ValorParcela);

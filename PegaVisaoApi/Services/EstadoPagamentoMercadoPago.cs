using System.Globalization;
using System.Text.Json;

namespace PegaVisaoApi.Services;

// Use somente com respostas da API autenticada, nunca com o body do webhook.
public record EstadoPagamentoMercadoPago(string? OrderId, string PaymentId,
    string ExternalReference, string Status, string StatusDetail, decimal Valor, bool Confirmado)
{
    public static EstadoPagamentoMercadoPago DaOrder(JsonElement order)
    {
        var pagamentos = order.GetProperty("transactions").GetProperty("payments");
        if (pagamentos.GetArrayLength() != 1)
            throw new InvalidDataException("Quantidade de transações inesperada.");
        var pagamento = pagamentos[0];
        var status = Texto(pagamento, "status");
        var detalhe = Texto(pagamento, "status_detail");
        var valor = ValorMonetario(order, "total_amount");
        if (valor <= 0 || ValorMonetario(pagamento, "amount") != valor)
            throw new InvalidDataException("Valor da transação diverge da order.");
        var confirmado = Texto(order, "status") == "processed"
            && Texto(order, "status_detail") == "accredited"
            && status == "processed" && detalhe == "accredited";
        return new(Texto(order, "id"), Texto(pagamento, "id"),
            Texto(order, "external_reference"), status, detalhe, valor, confirmado);
    }

    public static EstadoPagamentoMercadoPago DoPayment(JsonElement payment) => new(
        null, Texto(payment, "id"), Texto(payment, "external_reference"),
        Texto(payment, "status"), Texto(payment, "status_detail"),
        ValorMonetario(payment, "transaction_amount"),
        Texto(payment, "status") == "approved" && Texto(payment, "currency_id") == "BRL");

    private static string Texto(JsonElement json, string campo) => json.GetProperty(campo).ToString();
    private static decimal ValorMonetario(JsonElement json, string campo) =>
        decimal.Parse(Texto(json, campo), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
}

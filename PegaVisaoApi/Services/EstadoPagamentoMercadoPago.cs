using System.Globalization;
using System.Text.Json;

namespace PegaVisaoApi.Services;

// Aceita apenas respostas da API autenticada, nunca dados do body do webhook.
public record EstadoPagamentoMercadoPago(string? OrderId, string PaymentId,
    string ExternalReference, string Status, string StatusDetail, decimal Valor, bool Confirmado)
{
    public bool EncerradoSemPagamento { get; init; }

    public static EstadoPagamentoMercadoPago DaOrder(JsonElement order)
    {
        var statusOrder = Texto(order, "status");
        var valor = ValorMonetario(order, "total_amount");
        if (valor <= 0) throw new InvalidDataException("Valor da order inválido.");
        var encerrado = statusOrder is "canceled" or "cancelled" or "expired";
        var pagamentos = order.TryGetProperty("transactions", out var transactions)
            && transactions.TryGetProperty("payments", out var payments) ? payments : default;
        var quantidade = pagamentos.ValueKind == JsonValueKind.Array ? pagamentos.GetArrayLength() : 0;
        if (quantidade == 0 && (encerrado || statusOrder == "created"))
            return new(Texto(order, "id"), "", Texto(order, "external_reference"),
                statusOrder, TextoOpcional(order, "status_detail"), valor, false)
                { EncerradoSemPagamento = encerrado };
        if (quantidade != 1)
            throw new InvalidDataException("Quantidade de transações inesperada.");
        var pagamento = pagamentos[0];
        var status = Texto(pagamento, "status");
        var detalhe = Texto(pagamento, "status_detail");
        if (ValorMonetario(pagamento, "amount") != valor)
            throw new InvalidDataException("Valor da transação diverge da order.");
        var confirmado = statusOrder == "processed"
            && Texto(order, "status_detail") == "accredited"
            && status == "processed" && detalhe == "accredited";
        return new(Texto(order, "id"), Texto(pagamento, "id"),
            Texto(order, "external_reference"), status, detalhe, valor, confirmado)
            { EncerradoSemPagamento = encerrado && status != "processed" };
    }

    // Uma tentativa de cartão rejeitada não encerra necessariamente o checkout.
    public static EstadoPagamentoMercadoPago DoPayment(JsonElement payment) => new(
        null, Texto(payment, "id"), Texto(payment, "external_reference"),
        Texto(payment, "status"), Texto(payment, "status_detail"),
        ValorMonetario(payment, "transaction_amount"),
        Texto(payment, "status") == "approved" && Texto(payment, "currency_id") == "BRL");

    private static string Texto(JsonElement json, string campo) => json.GetProperty(campo).ToString();
    private static string TextoOpcional(JsonElement json, string campo) =>
        json.TryGetProperty(campo, out var value) ? value.ToString() : "";
    private static decimal ValorMonetario(JsonElement json, string campo) =>
        decimal.Parse(Texto(json, campo), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
}

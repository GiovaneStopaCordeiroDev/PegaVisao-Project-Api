namespace PegaVisaoApi.Models
{
    public enum Status
    {
        Pendente,
        Enviado,
        Entregue,
        Cancelado,
        Pago = 4
    }

    public class Pedido
    {
        public int Id { get; set; }
        public Guid? CheckoutSessionId { get; set; }
        public string? CheckoutHash { get; set; }
        public string? PaymentIdempotencyKey { get; set; }
        public string? PagamentoPayloadJson { get; set; }
        public DateTime? PagamentoLeaseAte { get; set; }
        public int? CupomId { get; set; }
        public virtual Cupom? Cupom { get; set; }
        public string? CupomCodigo { get; set; }
        public decimal ValorDesconto { get; set; }
        public bool ExcluidoPeloCliente { get; set; }
        public EstadoEstoquePedido EstadoEstoque { get; set; }
        public DateTime? ProximaConsultaEstoqueEm { get; set; }

        public DateTime? PagamentoExpiraEm { get; set; }

        public DateTime DataPedido { get; set; } = DateTime.UtcNow;

        public decimal ValorTotal { get; set; }
        public decimal ValorFrete { get; set; }
        public int? FreteServicoId { get; set; }
        public string? FreteServico { get; set; }
        public string? FreteTransportadora { get; set; }
        public int? FretePrazoDias { get; set; }
        public bool? FreteSandbox { get; set; }
        public Guid? FreteCotacaoId { get; set; }
        public string? FreteVolumesJson { get; set; }
        public string? MelhorEnvioOrderId { get; set; }
        public string? MelhorEnvioEtiquetaStatus { get; set; }
        public DateTime? MelhorEnvioEtiquetaGeradaEm { get; set; }
        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        public decimal SubtotalProdutos => ValorTotal - ValorFrete + ValorDesconto;

        public Status Status { get; set; } = Status.Pendente;


        public int UsuarioId { get; set; }

        public virtual Usuario Usuario { get; set; }


        public string Cep { get; set; }

        public string Rua { get; set; }

        public string Numero { get; set; }

        public string? Complemento { get; set; }

        public string Bairro { get; set; }

        public string Cidade { get; set; }

        public string Estado { get; set; }

        public string FormaPagamento { get; set; }

        public string? MercadoPagoOrderId { get; set; }

        public string? MercadoPagoStatus { get; set; }

        public string? MercadoPagoCheckoutUrl { get; set; }
        public string? MercadoPagoPaymentId { get; set; }
        public string? PixQrCode { get; set; }
        public string? PixQrCodeBase64 { get; set; }

        public virtual ICollection<ItemPedido> Itens { get; set; }
            = new List<ItemPedido>();
    }
}

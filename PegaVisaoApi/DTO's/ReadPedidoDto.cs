namespace PegaVisaoApi.DTO_s
{
    public class ReadPedidoDto
    {
        public int Id { get; set; }

        public DateTime? PagamentoExpiraEm { get; set; }
        public DateTime ServidorAgora => DateTime.UtcNow;

        public DateTime DataPedido { get; set; }

        public decimal ValorTotal { get; set; }
        public decimal ValorFrete { get; set; }
        public int? FreteServicoId { get; set; }
        public string? FreteServico { get; set; }
        public string? FreteTransportadora { get; set; }
        public int? FretePrazoDias { get; set; }
        public bool? FreteSandbox { get; set; }
        public Guid? FreteCotacaoId { get; set; }
        public decimal SubtotalProdutos { get; set; }

        public string Status { get; set; }

        public string FormaPagamento { get; set; }

        public string? MercadoPagoOrderId { get; set; }

        public string? MercadoPagoStatus { get; set; }

        public string? MercadoPagoCheckoutUrl { get; set; }

        public string? MercadoPagoPaymentId { get; set; }
        public string? PixQrCode { get; set; }
        public string? PixQrCodeBase64 { get; set; }

        public string Cep { get; set; }

        public string Rua { get; set; }

        public string Numero { get; set; }

        public string? Complemento { get; set; }

        public string Bairro { get; set; }

        public string Cidade { get; set; }

        public string Estado { get; set; }

        public List<ReadItemPedidoDto> Itens { get; set; }
    }
}
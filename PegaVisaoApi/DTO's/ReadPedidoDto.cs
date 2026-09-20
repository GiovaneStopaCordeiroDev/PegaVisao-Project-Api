namespace PegaVisaoApi.DTO_s
{
    public class ReadPedidoDto
    {
        public int Id { get; set; }

        public DateTime DataPedido { get; set; }

        public decimal ValorTotal { get; set; }

        public string Status { get; set; }

        public string FormaPagamento { get; set; }

        public string? MercadoPagoOrderId { get; set; }

        public string? MercadoPagoStatus { get; set; }

        public string? MercadoPagoCheckoutUrl { get; set; }

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
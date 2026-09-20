namespace PegaVisaoApi.Models
{
    public enum Status
    {
        Pendente,
        Enviado,
        Entregue,
        Cancelado
    }

    public class Pedido
    {
        public int Id { get; set; }

        public DateTime DataPedido { get; set; } = DateTime.UtcNow;

        public decimal ValorTotal { get; set; }

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

        public virtual ICollection<ItemPedido> Itens { get; set; }
            = new List<ItemPedido>();
    }
}
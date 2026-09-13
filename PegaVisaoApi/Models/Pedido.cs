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
        public virtual ICollection<ItemPedido> Itens { get; set; }
    }
}

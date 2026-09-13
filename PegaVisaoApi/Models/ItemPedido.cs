namespace PegaVisaoApi.Models
{
    public class ItemPedido
    {
        public int Id { get; set; }

        public int PedidoId { get; set; }

        public int VariacaoProdutoId { get; set; }

        public int Quantidade { get; set; }

        public decimal PrecoUnitario { get; set; }

        public virtual Pedido Pedido { get; set; }

        public virtual VariacaoProduto VariacaoProduto { get; set; }
    }
}

namespace PegaVisaoApi.Models
{
    public class VariacaoProduto
    {
        public int Id { get; set; }

        public string Cor { get; set; }

        public string Tamanho { get; set; }

        public int Estoque { get; set; }
        public int EstoqueReservado { get; set; }
        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        public int EstoqueDisponivel => Estoque - EstoqueReservado;

        public int ProdutoId { get; set; }

        public virtual Produto Produto { get; set; }
    }
}

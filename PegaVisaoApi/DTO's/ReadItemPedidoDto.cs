namespace PegaVisaoApi.DTO_s
{
    public class ReadItemPedidoDto
    {
        public int Id { get; set; }

        public int VariacaoProdutoId { get; set; }

        public int Quantidade { get; set; }

        public decimal PrecoUnitario { get; set; }

        public string NomeProduto { get; set; }

        public string ImagemProduto { get; set; }

        public string Cor { get; set; }

        public string Tamanho { get; set; }
    }
}
namespace PegaVisaoApi.DTO_s
{
    public class ReadItemPedidoDto
    {
        public int Id { get; set; }
        public int VariacaoProdutoId { get; set; }
        public int Quantidade { get; set; }
        public decimal PrecoUnitario { get; set; }
    }
}

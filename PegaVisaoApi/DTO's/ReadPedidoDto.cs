namespace PegaVisaoApi.DTO_s
{
    public class ReadPedidoDto
    {
        public int Id { get; set; }
        public DateTime DataPedido { get; set; }
        public decimal ValorTotal { get; set; }
        public string Status { get; set; }
        public List<ReadItemPedidoDto> Itens { get; set; }
    }
}

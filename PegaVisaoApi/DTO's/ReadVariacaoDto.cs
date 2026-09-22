namespace PegaVisaoApi.DTO_s
{
    public class ReadVariacaoDto
    {
        public int Id { get; set; }
        public string Cor { get; set; } = string.Empty;
        public string Tamanho { get; set; } = string.Empty;
        public int Estoque { get; set; }
        public int EstoqueReservado { get; set; }
        public int EstoqueDisponivel { get; set; }
    }
}

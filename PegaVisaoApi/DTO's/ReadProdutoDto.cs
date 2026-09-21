namespace PegaVisaoApi.DTO_s
{
    public class ReadProdutoDto
    {
        public int Id { get; set; }
        public string Nome { get; set; } = string.Empty;
        public string Descricao { get; set; } = string.Empty;
        public decimal Preco { get; set; }
        public decimal? PesoKg { get; set; }
        public decimal? AlturaCm { get; set; }
        public decimal? LarguraCm { get; set; }
        public decimal? ComprimentoCm { get; set; }

        public string ImagemPrincipal { get; set; } = string.Empty;

        public int CategoriaId { get; set; }

        public List<ReadVariacaoDto> Variacoes { get; set; } = new();
    }
}

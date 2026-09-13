namespace PegaVisaoApi.DTO_s
{
    public class ReadCategoriaDto
    {
        public int Id { get; set; }
        public string Nome { get; set; } = string.Empty;

        public List<ReadProdutoDto> Produtos { get; set; } = new();
    }
}

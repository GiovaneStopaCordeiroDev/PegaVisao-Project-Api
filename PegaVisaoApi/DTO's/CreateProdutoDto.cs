using System.ComponentModel.DataAnnotations;

namespace PegaVisaoApi.DTO_s
{
    public class CreateProdutoDto : DadosEnvioProdutoDto
    {

        [Required(ErrorMessage = "O campo Nome é obrigatório.")]
        public string Nome { get; set; }

        [StringLength(200, ErrorMessage = "O campo Descricao deve ter no máximo 200 caracteres.")]
        public string Descricao { get; set; }

        [Required(ErrorMessage = "O campo Preco é obrigatório.")]
        public decimal Preco { get; set; }

        [Required(ErrorMessage = "O campo ImagemPrincipal é obrigatório.")]
        public string ImagemPrincipal { get; set; }

        [Required(ErrorMessage = "O campo Categoria é obrigatório.")]
        public int CategoriaId { get; set; }

        public List<CreateVariacaoDto> Variacoes { get; set; } = new();
    }
}

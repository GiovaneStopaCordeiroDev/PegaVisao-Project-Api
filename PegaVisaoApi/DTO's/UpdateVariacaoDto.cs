using System.ComponentModel.DataAnnotations;

namespace PegaVisaoApi.DTO_s
{
    public class UpdateVariacaoDto
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "O campo Cor é obrigatório.")]
        public string Cor { get; set; }

        [Required(ErrorMessage = "O campo Tamanho é obrigatório.")]
        public string Tamanho { get; set; }

        [Required(ErrorMessage = "O campo Estoque é obrigatório.")]
        public int Estoque { get; set; }
    }
}

using System.ComponentModel.DataAnnotations;

namespace PegaVisaoApi.DTO_s
{
    public class CreateCategoriaDto
    {
        [Required(ErrorMessage = "O campo Nome é obrigatório.")]
        [StringLength(50, ErrorMessage = "O campo Nome deve ter no máximo 50 caracteres.")]
        public string Nome { get; set; }
    }
}

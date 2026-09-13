using System.ComponentModel.DataAnnotations;

namespace PegaVisaoApi.DTO_s
{
    public class CreateItemPedidoDto
    {
        public int VariacaoProdutoId { get; set; }

        [Required(ErrorMessage = "O campo Quantidade é obrigatório.")]
        public int Quantidade { get; set; }
    }
}

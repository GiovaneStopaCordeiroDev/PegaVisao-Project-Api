using System.ComponentModel.DataAnnotations;

namespace PegaVisaoApi.DTO_s
{
    public class CreatePedidoDto
    {
        [Required(ErrorMessage = "O campo Itens é obrigatório.")]
        public List<CreateItemPedidoDto> Itens { get; set; }
    }
}

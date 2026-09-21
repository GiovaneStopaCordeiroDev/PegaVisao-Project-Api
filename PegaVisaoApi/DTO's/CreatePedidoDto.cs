using System.ComponentModel.DataAnnotations;

namespace PegaVisaoApi.DTO_s
{
    public class CreatePedidoDto
    {
        [Required(ErrorMessage = "O campo Itens é obrigatório.")]
        public List<CreateItemPedidoDto> Itens { get; set; }

        [Required]
        public Guid? CotacaoFreteId { get; set; }
        [Range(1, int.MaxValue)]
        public int FreteServicoId { get; set; }

        [Required(ErrorMessage = "O CEP é obrigatório.")]
        public string Cep { get; set; }

        [Required(ErrorMessage = "A rua é obrigatória.")]
        public string Rua { get; set; }

        [Required(ErrorMessage = "O número é obrigatório.")]
        public string Numero { get; set; }

        public string? Complemento { get; set; }

        [Required(ErrorMessage = "O bairro é obrigatório.")]
        public string Bairro { get; set; }

        [Required(ErrorMessage = "A cidade é obrigatória.")]
        public string Cidade { get; set; }

        [Required(ErrorMessage = "O estado é obrigatório.")]
        public string Estado { get; set; }

        [Required(ErrorMessage = "A forma de pagamento é obrigatória.")]
        public string FormaPagamento { get; set; }
    }
}
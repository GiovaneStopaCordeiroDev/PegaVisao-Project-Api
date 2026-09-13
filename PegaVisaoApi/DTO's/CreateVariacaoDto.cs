using System.ComponentModel.DataAnnotations;

namespace PegaVisaoApi.DTO_s
{
    public class CreateVariacaoDto
    {
            
        public string Cor { get; set; }

        public string Tamanho { get; set; }

        public int Estoque { get; set; }
        
    }
}

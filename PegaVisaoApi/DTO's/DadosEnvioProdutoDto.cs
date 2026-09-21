using System.ComponentModel.DataAnnotations;

namespace PegaVisaoApi.DTO_s;

public class DadosEnvioProdutoDto
{
    [Required, Range(typeof(decimal), "0.001", "1000")]
    public decimal? PesoKg { get; set; }
    [Required, Range(typeof(decimal), "0.1", "1000")]
    public decimal? AlturaCm { get; set; }
    [Required, Range(typeof(decimal), "0.1", "1000")]
    public decimal? LarguraCm { get; set; }
    [Required, Range(typeof(decimal), "0.1", "1000")]
    public decimal? ComprimentoCm { get; set; }
}

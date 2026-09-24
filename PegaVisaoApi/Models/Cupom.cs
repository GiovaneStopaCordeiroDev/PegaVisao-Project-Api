using System.ComponentModel.DataAnnotations;
namespace PegaVisaoApi.Models;

public class Cupom
{
    public int Id { get; set; }
    [MaxLength(40)] public string Codigo { get; set; } = "";
    [MaxLength(300)] public string Descricao { get; set; } = "";
    [MaxLength(20)] public string Tipo { get; set; } = "Percentual";
    public decimal Valor { get; set; }
    public decimal ValorMinimo { get; set; }
    public decimal? DescontoMaximo { get; set; }
    public DateTime InicioEm { get; set; }
    public DateTime ValidadeEm { get; set; }
    public bool Ativo { get; set; } = true;
    public bool Excluido { get; set; }
    public int? LimiteTotal { get; set; }
    public int? LimitePorUsuario { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
    public DateTime AtualizadoEm { get; set; } = DateTime.UtcNow;
}

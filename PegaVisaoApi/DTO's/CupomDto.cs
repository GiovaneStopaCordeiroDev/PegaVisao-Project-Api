using System.ComponentModel.DataAnnotations;
namespace PegaVisaoApi.DTO_s;
public class CupomDto : IValidatableObject
{
    [Required, RegularExpression("^[A-Za-z0-9_-]{3,40}$")] public string Codigo { get; set; } = "";
    [StringLength(300)] public string Descricao { get; set; } = "";
    [Required, RegularExpression("^(Percentual|Fixo)$")] public string Tipo { get; set; } = "Percentual";
    [Range(typeof(decimal), "0.01", "99999999", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)] public decimal Valor { get; set; }
    [Range(typeof(decimal), "0", "99999999", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)] public decimal ValorMinimo { get; set; }
    [Range(typeof(decimal), "0.01", "99999999", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)] public decimal? DescontoMaximo { get; set; }
    public DateTime InicioEm { get; set; }
    public DateTime ValidadeEm { get; set; }
    public bool TodosProdutos { get; set; } = true;
    [MaxLength(1000)] public List<int> ProdutoIds { get; set; } = new();
    public bool Ativo { get; set; } = true;
    [Range(1, int.MaxValue)] public int? LimiteTotal { get; set; }
    [Range(1, int.MaxValue)] public int? LimitePorUsuario { get; set; }
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (ProdutoIds == null || ProdutoIds.Any(id => id <= 0) || (!TodosProdutos && ProdutoIds.Count == 0))
            yield return new("Selecione pelo menos um produto válido para o cupom restrito.");
        if (Tipo == "Percentual" && Valor > 100) yield return new("Percentual deve ser de até 100%.");
        if (InicioEm.Kind != DateTimeKind.Utc || ValidadeEm.Kind != DateTimeKind.Utc || ValidadeEm <= InicioEm)
            yield return new("Informe datas UTC válidas, com validade posterior ao início.");
        if (decimal.Round(Valor, 2) != Valor || decimal.Round(ValorMinimo, 2) != ValorMinimo ||
            DescontoMaximo.HasValue && decimal.Round(DescontoMaximo.Value, 2) != DescontoMaximo)
            yield return new("Use no máximo duas casas decimais.");
    }
}
public record ValidarCupomDto([Required, StringLength(40)] string Codigo, [Required] List<CreateItemPedidoDto> Itens);

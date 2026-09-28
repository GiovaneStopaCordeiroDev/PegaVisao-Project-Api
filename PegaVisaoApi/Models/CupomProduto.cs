namespace PegaVisaoApi.Models;

public class CupomProduto
{
    public int CupomId { get; set; }
    public Cupom Cupom { get; set; } = null!;
    public int ProdutoId { get; set; }
    public Produto Produto { get; set; } = null!;
}
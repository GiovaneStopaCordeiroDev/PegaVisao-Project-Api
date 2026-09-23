namespace PegaVisaoApi.Models;

public class AvaliacaoProduto
{
    public int Id { get; set; }
    public int ProdutoId { get; set; }
    public int UsuarioId { get; set; }
    public int Nota { get; set; }
    public DateTime AtualizadaEm { get; set; } = DateTime.UtcNow;
    public virtual Produto Produto { get; set; } = null!;
    public virtual Usuario Usuario { get; set; } = null!;
}

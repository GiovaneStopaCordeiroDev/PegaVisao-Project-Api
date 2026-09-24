namespace PegaVisaoApi.Models;
public class TentativaPagamento
{
    public int Id { get; set; }
    public int PedidoId { get; set; }
    public virtual Pedido Pedido { get; set; } = null!;
    public string IdempotencyKey { get; set; } = "";
    public string Status { get; set; } = "Iniciada";
    public int? HttpStatus { get; set; }
    public string? CodigoErro { get; set; }
    public DateTime CriadaEm { get; set; } = DateTime.UtcNow;
    public DateTime? FinalizadaEm { get; set; }
}

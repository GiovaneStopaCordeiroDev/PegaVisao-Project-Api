namespace PegaVisaoApi.Models;

public class CotacaoFrete
{
    public Guid Id { get; set; }
    public int UsuarioId { get; set; }
    public string CepDestino { get; set; } = "";
    public string CarrinhoHash { get; set; } = "";
    public string ConexaoId { get; set; } = "";
    public bool Sandbox { get; set; }
    public decimal Subtotal { get; set; }
    public string OpcoesJson { get; set; } = "";
    public DateTime CriadaEm { get; set; }
    public DateTime ExpiraEm { get; set; }
    public DateTime? ConsumidaEm { get; set; }
}

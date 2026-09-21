namespace PegaVisaoApi.Models;

// Uma conexão da loja por ambiente/aplicativo. Nunca retornar esta entidade pela API.
public class MelhorEnvioConexao
{
    public string Id { get; set; } = "";
    public string? AccessTokenProtegido { get; set; }
    public string? RefreshTokenProtegido { get; set; }
    public DateTime? ExpiraEm { get; set; }
    public DateTime AtualizadoEm { get; set; }
    public string? StateHash { get; set; }
    public string? NavegadorHash { get; set; }
    public DateTime? StateExpiraEm { get; set; }
    public int? AdministradorId { get; set; }
}

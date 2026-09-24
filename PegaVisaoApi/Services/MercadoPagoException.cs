namespace PegaVisaoApi.Services;
public sealed class MercadoPagoException(int status, string codigo, string mensagem) : Exception(mensagem)
{
    public int Status { get; } = status;
    public string Codigo { get; } = codigo;
}

namespace PegaVisaoApi.Services;

public static class PrazoPagamento
{
    public static bool DeveCancelar(DateTime? expiraEm, DateTime agora, string? statusOrder) =>
        expiraEm.HasValue && expiraEm <= agora && statusOrder is "created" or "action_required";
}

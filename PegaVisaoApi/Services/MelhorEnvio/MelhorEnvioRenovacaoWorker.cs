namespace PegaVisaoApi.Services.MelhorEnvio;

public sealed class MelhorEnvioRenovacaoWorker(IServiceScopeFactory scopes,
    ILogger<MelhorEnvioRenovacaoWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Não impede a inicialização da API/Pix quando a integração ainda não foi configurada.
        using var timer = new PeriodicTimer(TimeSpan.FromHours(12));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<MelhorEnvioService>().RenovarSeNecessarioAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                // Não registrar exceção completa: a integração manipula credenciais e tokens.
                logger.LogWarning("Melhor Envio: renovação não executada ({Tipo}). Verifique configuração, migration e conexão pelo painel.", ex.GetType().Name);
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

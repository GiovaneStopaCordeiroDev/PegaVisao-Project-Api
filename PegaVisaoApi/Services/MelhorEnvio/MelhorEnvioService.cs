using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using PegaVisaoApi.Data;
using PegaVisaoApi.Models;

namespace PegaVisaoApi.Services.MelhorEnvio;

public sealed class MelhorEnvioService(PegaVisaoContext db, IOptions<MelhorEnvioOptions> options,
    MelhorEnvioOAuthClient oauth, MelhorEnvioProtecao protecao, ILogger<MelhorEnvioService> logger)
{
    private readonly MelhorEnvioOptions _options = options.Value;
    public string Ambiente => _options.Sandbox ? "Sandbox" : "Produção";

    public async Task<string> CriarAutorizacaoAsync(int adminId, CancellationToken ct)
    {
        _options.Validar();
        if (!await db.Usuarios.AnyAsync(u => u.Id == adminId && u.IsAdmin, ct))
            throw new MelhorEnvioException(403, "Somente administradores podem conectar a loja.");
        await using var tx = await BloquearAsync(ct);
        var row = await db.MelhorEnvioConexoes.FindAsync([_options.ConexaoId], ct);
        if (row == null)
        {
            row = new MelhorEnvioConexao { Id = _options.ConexaoId };
            db.MelhorEnvioConexoes.Add(row);
        }
        var state = MelhorEnvioProtecao.GerarSegredo();
        row.StateHash = MelhorEnvioProtecao.Hash(state);
        row.NavegadorHash = null;
        row.StateExpiraEm = DateTime.UtcNow.AddMinutes(10);
        row.AdministradorId = adminId;
        row.AtualizadoEm = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        // A navegação para o backend cria cookie first-party; não depende de cookies third-party no React.
        var uri = new UriBuilder(_options.RedirectUri) { Path = "/api/MelhorEnvio/iniciar", Query = "" };
        return QueryHelpers.AddQueryString(uri.Uri.AbsoluteUri, "ticket", state);
    }

    public async Task<(string Url, string Cookie)> IniciarAsync(string ticket, CancellationToken ct)
    {
        _options.Validar();
        if (ticket.Length != 64) throw new MelhorEnvioException(400, "Link de autorização inválido.");
        await using var tx = await BloquearAsync(ct);
        var row = await db.MelhorEnvioConexoes.FindAsync([_options.ConexaoId], ct);
        ValidarState(row, ticket);
        if (row!.NavegadorHash != null) throw new MelhorEnvioException(400, "Link já utilizado. Inicie uma nova autorização.");
        var cookie = MelhorEnvioProtecao.GerarSegredo();
        row.NavegadorHash = MelhorEnvioProtecao.Hash(cookie);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return (oauth.UrlAutorizacao(ticket), cookie);
    }

    public async Task ConcluirAsync(string? state, string? cookie, string? code, string? error, CancellationToken ct)
    {
        _options.Validar();
        if (state?.Length != 64) throw new MelhorEnvioException(400, "State de autorização inválido.");
        await using var tx = await BloquearAsync(ct);
        var row = await db.MelhorEnvioConexoes.FindAsync([_options.ConexaoId], ct);
        ValidarState(row, state);
        if (!MelhorEnvioProtecao.Confere(cookie, row!.NavegadorHash))
            throw new MelhorEnvioException(400, "Autorização não pertence a este navegador. Inicie novamente pelo painel.");
        if (!await db.Usuarios.AnyAsync(u => u.Id == row.AdministradorId && u.IsAdmin, ct))
            throw new MelhorEnvioException(403, "O administrador não possui mais acesso à integração.");
        row.StateHash = null;
        row.StateExpiraEm = null;
        row.NavegadorHash = null;
        row.AdministradorId = null;
        MelhorEnvioTokens tokens;
        try
        {
            if (!string.IsNullOrEmpty(error)) throw new MelhorEnvioException(400, "Autorização não concedida. Você pode tentar novamente no painel.");
            if (string.IsNullOrWhiteSpace(code) || code.Length > 8192) throw new MelhorEnvioException(400, "Código de autorização ausente ou inválido.");
            // Não repete automaticamente uma troca de código: o código é de uso único.
            tokens = await oauth.TrocarCodigoAsync(code, ct);
        }
        catch
        {
            // Consome a tentativa também em erro/negação. Tokens antigos permanecem, se existirem.
            await db.SaveChangesAsync(CancellationToken.None);
            await tx.CommitAsync(CancellationToken.None);
            throw;
        }
        // Se persistir falhar após a troca externa, reiniciar a autorização: OAuth e banco
        // não compartilham uma transação. Não reutilizar o código nem exibir os tokens.
        SalvarTokens(row, tokens);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        logger.LogInformation("Melhor Envio conectado no ambiente {Ambiente}", Ambiente);
    }

    public async Task<object> StatusAsync(CancellationToken ct)
    {
        _options.Validar();
        var row = await db.MelhorEnvioConexoes.AsNoTracking().SingleOrDefaultAsync(p => p.Id == _options.ConexaoId, ct);
        return new { ambiente = Ambiente, conectado = row?.AccessTokenProtegido != null,
            tokenValido = row?.AccessTokenProtegido != null && row.ExpiraEm > DateTime.UtcNow,
            expiraEm = row?.ExpiraEm, atualizadoEm = row?.AtualizadoEm, cepOrigem = _options.CepOrigem };
    }

    // Futuras cotações devem obter o token aqui; não acessar diretamente o banco/configuração.
    public async Task<string> ObterAccessTokenAsync(CancellationToken ct)
    {
        _options.Validar();
        await using var tx = await BloquearAsync(ct);
        var row = await db.MelhorEnvioConexoes.FindAsync([_options.ConexaoId], ct);
        if (row?.RefreshTokenProtegido == null || row.AccessTokenProtegido == null)
            throw new MelhorEnvioException(409, "Conecte a loja ao Melhor Envio pelo painel administrativo.");
        if (row.ExpiraEm > DateTime.UtcNow.AddDays(3))
            return protecao.Desproteger(row.AccessTokenProtegido, "access");
        try
        {
            var tokens = await oauth.RenovarAsync(protecao.Desproteger(row.RefreshTokenProtegido, "refresh"), ct);
            SalvarTokens(row, tokens); // Salva também o refresh token rotacionado.
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            logger.LogInformation("Melhor Envio tokens renovados no ambiente {Ambiente}", Ambiente);
            return tokens.AccessToken;
        }
        catch (MelhorEnvioException ex) when (ex.HttpStatus == 409)
        {
            row.AccessTokenProtegido = null;
            row.RefreshTokenProtegido = null;
            row.ExpiraEm = null;
            row.AtualizadoEm = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            throw;
        }
    }

    public async Task RenovarSeNecessarioAsync(CancellationToken ct)
    {
        _options.Validar();
        if (await db.MelhorEnvioConexoes.AsNoTracking().AnyAsync(p => p.Id == _options.ConexaoId &&
            p.RefreshTokenProtegido != null && p.ExpiraEm <= DateTime.UtcNow.AddDays(3), ct))
            await ObterAccessTokenAsync(ct);
    }

    private void SalvarTokens(MelhorEnvioConexao row, MelhorEnvioTokens tokens)
    {
        row.AccessTokenProtegido = protecao.Proteger(tokens.AccessToken, "access");
        row.RefreshTokenProtegido = protecao.Proteger(tokens.RefreshToken, "refresh");
        row.ExpiraEm = DateTime.UtcNow.AddSeconds(tokens.ExpiresIn);
        row.AtualizadoEm = DateTime.UtcNow;
    }

    private static void ValidarState(MelhorEnvioConexao? row, string? state)
    {
        if (row == null || !MelhorEnvioProtecao.StateValido(state, row.StateHash, row.StateExpiraEm, DateTime.UtcNow))
            throw new MelhorEnvioException(400, "Autorização expirada, inválida ou já utilizada. Inicie novamente pelo painel.");
    }

    private async Task<IDbContextTransaction> BloquearAsync(CancellationToken ct)
    {
        var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            // Bloqueio no PostgreSQL funciona entre réplicas e também protege a primeira inserção.
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({_options.ConexaoId + ":MelhorEnvio"}))", ct);
            return tx;
        }
        catch { await tx.DisposeAsync(); throw; }
    }
}

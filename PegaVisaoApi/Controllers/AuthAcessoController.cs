using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PegaVisaoApi.Data.Dtos.AuthDtos;
using PegaVisaoApi.Models;
using PegaVisaoApi.Services;

namespace PegaVisaoApi.Controllers;

public partial class AuthController
{
    public sealed record EsqueciSenhaDto([Required, EmailAddress, MaxLength(254)] string Email);
    public sealed record RedefinirSenhaDto([Required, EmailAddress, MaxLength(254)] string Email,
        [Required, StringLength(64, MinimumLength = 64)] string Token,
        [Required, StringLength(72, MinimumLength = 8)] string Senha);
    public sealed record GoogleDto([Required, MaxLength(8192)] string Credencial, [MaxLength(72)] string? SenhaAtual);

    internal static bool SenhaPermitida(string? senha) => senha is { Length: >= 8 } && Encoding.UTF8.GetByteCount(senha) <= 72;
    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private Task BloquearEmail(string email) => _context.Database.ExecuteSqlInterpolatedAsync(
        $"SELECT pg_advisory_xact_lock(hashtextextended({email}, 0))");
    private IActionResult Sessao(Usuario usuario) => Ok(new UsuarioLogadoDto {
        Id = usuario.Id, Nome = usuario.Nome, Email = usuario.Email, IsAdmin = usuario.IsAdmin, Token = GerarToken(usuario)
    });

    [HttpPost("esqueci-senha")]
    public async Task<IActionResult> EsqueciSenha(EsqueciSenhaDto dto, [FromServices] IEmailRecuperacao emailService,
        [FromServices] ILogger<AuthController> logger, CancellationToken ct)
    {
        if (!emailService.Configurado)
            return StatusCode(503, new { mensagem = "A recuperação por e-mail está temporariamente indisponível." });
        var email = dto.Email.Trim().ToLowerInvariant();
        var agora = DateTime.UtcNow;
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var hash = Hash(token);
        var ids = await _context.Usuarios.Where(u => u.Email.ToLower() == email).Select(u => u.Id).Take(2).ToListAsync(ct);
        var id = ids.Count == 1 ? ids[0] : -1;
        var atualizados = await _context.Usuarios.Where(u => u.Id == id &&
            (u.RecuperacaoEnviadaEm == null || u.RecuperacaoEnviadaEm < agora.AddMinutes(-2)))
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.RecuperacaoHash, hash)
                .SetProperty(u => u.RecuperacaoExpiraEm, agora.AddMinutes(30))
                .SetProperty(u => u.RecuperacaoEnviadaEm, agora), ct);
        if (atualizados > 0)
        {
            // Fragmento não é enviado nos requests HTTP nem em Referer.
            var link = _configuration["Auth:FrontendUrl"]!.TrimEnd('/') + "/redefinir-senha#email=" +
                Uri.EscapeDataString(email) + "&token=" + token;
            try { await emailService.Enviar(email, link, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // Não registrar mensagem de SMTP, destinatário ou link secreto.
                logger.LogError("Falha no envio da recuperação: {Tipo}", ex.GetType().Name);
                await _context.Usuarios.Where(u => u.Email.ToLower() == email && u.RecuperacaoHash == hash)
                    .ExecuteUpdateAsync(s => s.SetProperty(u => u.RecuperacaoHash, (string?)null)
                        .SetProperty(u => u.RecuperacaoExpiraEm, (DateTime?)null)
                        .SetProperty(u => u.RecuperacaoEnviadaEm, (DateTime?)null), ct);
            }
        }
        return Ok(new { mensagem = "Se o e-mail estiver cadastrado, você receberá um link para redefinir a senha. Confira também o spam e aguarde 2 minutos antes de solicitar novamente." });
    }

    [HttpPost("redefinir-senha")]
    public async Task<IActionResult> RedefinirSenha(RedefinirSenhaDto dto, CancellationToken ct)
    {
        if (!SenhaPermitida(dto.Senha)) return BadRequest(new { mensagem = "Use ao menos 8 caracteres e no máximo 72 bytes na senha." });
        var hash = Hash(dto.Token);
        var agora = DateTime.UtcNow;
        var email = dto.Email.Trim().ToLowerInvariant();
        var senhaHash = BCrypt.Net.BCrypt.HashPassword(dto.Senha);
        // Uma única atualização condicional impede reutilização e consumo concorrente.
        var alterados = await _context.Usuarios.Where(u => u.Email.ToLower() == email &&
            u.RecuperacaoHash == hash && u.RecuperacaoExpiraEm > agora)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.SenhaHash, senhaHash)
                .SetProperty(u => u.RecuperacaoHash, (string?)null)
                .SetProperty(u => u.RecuperacaoExpiraEm, (DateTime?)null)
                .SetProperty(u => u.VersaoSessao, u => u.VersaoSessao + 1), ct);
        return alterados == 1 ? Ok(new { mensagem = "Senha atualizada. Entre novamente com sua nova senha." }) :
            BadRequest(new { mensagem = "Link inválido ou expirado. Solicite uma nova recuperação de senha." });
    }

    [HttpPost("google")]
    public async Task<IActionResult> GoogleLogin(GoogleDto dto, [FromServices] IGoogleIdentidade google, CancellationToken ct)
    {
        var clientId = _configuration["Google:ClientId"];
        if (string.IsNullOrWhiteSpace(clientId))
            return StatusCode(503, new { mensagem = "O acesso com Google ainda não está disponível." });
        GoogleJsonWebSignature.Payload identidade;
        try {
            identidade = await google.Validar(dto.Credencial, clientId);
        }
        catch (InvalidJwtException) { return Unauthorized(new { mensagem = "A autenticação do Google expirou ou é inválida. Tente novamente." }); }
        if (!identidade.EmailVerified || string.IsNullOrWhiteSpace(identidade.Subject) ||
            string.IsNullOrWhiteSpace(identidade.Email)) return Unauthorized();

        var email = identidade.Email.Trim().ToLowerInvariant();
        await using var transacao = await _context.Database.BeginTransactionAsync(ct);
        await BloquearEmail(email);
        var usuario = await _context.Usuarios.SingleOrDefaultAsync(u => u.GoogleSubject == identidade.Subject, ct);
        if (usuario == null)
        {
            var existentes = await _context.Usuarios.Where(u => u.Email.ToLower() == email).Take(2).ToListAsync(ct);
            if (existentes.Count > 1) return Conflict(new { mensagem = "Entre com e-mail e senha e entre em contato com o suporte para vincular o Google." });
            usuario = existentes.SingleOrDefault();
            if (usuario != null)
            {
                // Não vincular contas existentes apenas pela coincidência do e-mail.
                if (usuario.GoogleSubject != null) return Conflict(new { mensagem = "Esta conta já possui outro vínculo com Google." });
                if (string.IsNullOrEmpty(dto.SenhaAtual)) return Conflict(new {
                    codigo = "VINCULO_REQUER_SENHA", mensagem = "Este e-mail já possui cadastro. Confirme a senha da loja para vincular o Google." });
                if (string.IsNullOrEmpty(usuario.SenhaHash) || !BCrypt.Net.BCrypt.Verify(dto.SenhaAtual, usuario.SenhaHash))
                    return Unauthorized(new { mensagem = "A senha da loja está incorreta." });
            }
            else
            {
                usuario = new Usuario { Email = email, Nome = string.IsNullOrWhiteSpace(identidade.Name) ? email : identidade.Name, IsAdmin = false };
                _context.Usuarios.Add(usuario);
            }
            usuario.GoogleSubject = identidade.Subject;
            await _context.SaveChangesAsync(ct);
        }
        await transacao.CommitAsync(ct);
        return Sessao(usuario);
    }
}
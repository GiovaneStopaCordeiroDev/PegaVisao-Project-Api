using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using System.Net.Http.Headers;

namespace PegaVisaoApi.Services;

public interface IEmailRecuperacao
{
    bool Configurado { get; }
    Task Enviar(string destinatario, string link, CancellationToken ct);
}

public sealed class EmailRecuperacao(IConfiguration config, HttpClient http) : IEmailRecuperacao
{
    private bool UsarSmtp => string.Equals(config["Email:Provedor"], "Smtp", StringComparison.OrdinalIgnoreCase);
    public bool Configurado =>
        Uri.TryCreate(config["Auth:FrontendUrl"], UriKind.Absolute, out var url) &&
        (url.Scheme == "https" || (url.Scheme == "http" && url.IsLoopback)) &&
        string.IsNullOrEmpty(url.Query) && string.IsNullOrEmpty(url.Fragment) &&
        !string.IsNullOrWhiteSpace(config["Email:Remetente"]) &&
        (UsarSmtp ? new[] { "Email:Host", "Email:Usuario", "Email:Senha" }.All(k => !string.IsNullOrWhiteSpace(config[k])) :
            (string.IsNullOrEmpty(config["Email:Provedor"]) || string.Equals(config["Email:Provedor"], "Resend", StringComparison.OrdinalIgnoreCase)) &&
            !string.IsNullOrWhiteSpace(config["Email:ApiKey"]));

    public async Task Enviar(string destinatario, string link, CancellationToken ct)
    {
        if (!Configurado) throw new InvalidOperationException("Envio de recuperação não configurado.");
        const string assunto = "Redefina sua senha — PegaVisão";
        var texto = $"Recebemos uma solicitação para alterar sua senha.\n\nAcesse o link em até 30 minutos:\n{link}\n\nEste link só pode ser usado uma vez. Se você não solicitou a alteração, ignore este e-mail.";
        if (!UsarSmtp)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config["Email:ApiKey"]);
            request.Content = JsonContent.Create(new { from = config["Email:Remetente"], to = new[] { destinatario }, subject = assunto, text = texto });
            using var response = await http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            return;
        }
        var mensagem = new MimeMessage();
        mensagem.From.Add(new MailboxAddress("PegaVisão", config["Email:Remetente"]!));
        mensagem.To.Add(MailboxAddress.Parse(destinatario));
        mensagem.Subject = assunto;
        mensagem.Body = new TextPart("plain") { Text = texto };
        using var smtp = new SmtpClient { Timeout = 15000 };
        var porta = config.GetValue("Email:Porta", 587);
        await smtp.ConnectAsync(config["Email:Host"]!, porta,
            porta == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls, ct);
        await smtp.AuthenticateAsync(config["Email:Usuario"]!, config["Email:Senha"]!, ct);
        await smtp.SendAsync(mensagem, ct);
        await smtp.DisconnectAsync(true, ct);
    }
}
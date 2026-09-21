using System.Net.Mail;

namespace PegaVisaoApi.Services.MelhorEnvio;

public sealed class MelhorEnvioOptions
{
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public bool Sandbox { get; set; } = true;
    public string RedirectUri { get; set; } = "";
    public string CepOrigem { get; set; } = "17013113";
    public string EmailContato { get; set; } = "";
    public string TokenEncryptionKey { get; set; } = "";
    public string BaseUrl => Sandbox ? "https://sandbox.melhorenvio.com.br" : "https://melhorenvio.com.br";
    public string ConexaoId => $"{(Sandbox ? "sandbox" : "producao")}:{ClientId}";

    public void Validar()
    {
        if (!int.TryParse(ClientId, out var id) || id <= 0 || string.IsNullOrWhiteSpace(ClientSecret))
            throw new MelhorEnvioException(503, "Configure ClientId e ClientSecret do Melhor Envio no backend.");
        if (!Uri.TryCreate(RedirectUri, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
            !string.IsNullOrEmpty(uri.UserInfo) || uri.AbsolutePath != "/api/MelhorEnvio/callback")
            throw new MelhorEnvioException(503, "Configure RedirectUri HTTPS terminando em /api/MelhorEnvio/callback.");
        if (!MailAddress.TryCreate(EmailContato, out var email) || email.Address != EmailContato ||
            EmailContato.IndexOfAny(['\r', '\n', '(', ')']) >= 0)
            throw new MelhorEnvioException(503, "Configure MelhorEnvio:EmailContato com seu e-mail de suporte.");
        try
        {
            if (Convert.FromBase64String(TokenEncryptionKey).Length != 32) throw new FormatException();
        }
        catch (FormatException)
        {
            throw new MelhorEnvioException(503, "Configure TokenEncryptionKey com uma chave Base64 de 32 bytes e mantenha-a entre deploys.");
        }
    }
}

public sealed class MelhorEnvioException(int httpStatus, string message) : Exception(message)
{
    public int HttpStatus { get; } = httpStatus;
}

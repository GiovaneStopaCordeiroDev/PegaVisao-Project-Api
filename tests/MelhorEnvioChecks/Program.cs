using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PegaVisaoApi.Controllers;
using PegaVisaoApi.Data;
using PegaVisaoApi.Services.MelhorEnvio;

var count = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception(name);
    count++;
    Console.WriteLine($"OK: {name}");
}
var config = new MelhorEnvioOptions
{
    ClientId = "123", ClientSecret = "fake-secret-for-test", Sandbox = true,
    RedirectUri = "https://api.example.com/api/MelhorEnvio/callback",
    EmailContato = "suporte@example.com", TokenEncryptionKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
};
var options = Options.Create(config);
config.Validar();
Check(true, "Configuração válida aceita");
var protection = new MelhorEnvioProtecao(options);
var encrypted = protection.Proteger("fake-access-token", "access");
Check(encrypted != "fake-access-token" && protection.Desproteger(encrypted, "access") == "fake-access-token", "Token criptografado e recuperado");
Check(encrypted != protection.Proteger("fake-access-token", "access"), "Nonce diferente a cada criptografia");
void RejectCrypto(Action action, string name)
{
    try { action(); throw new Exception("Proteção deveria rejeitar: " + name); }
    catch (CryptographicException) { Check(true, name); }
}
var corrupted = Convert.FromBase64String(encrypted);
corrupted[^1] ^= 1;
RejectCrypto(() => protection.Desproteger(Convert.ToBase64String(corrupted), "access"), "Token adulterado rejeitado");
RejectCrypto(() => protection.Desproteger(encrypted, "refresh"), "Token não pode trocar de finalidade");
config.Sandbox = false;
RejectCrypto(() => protection.Desproteger(encrypted, "access"), "Token Sandbox não funciona em produção");
config.Sandbox = true;
var oldKey = config.TokenEncryptionKey;
config.TokenEncryptionKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
RejectCrypto(() => protection.Desproteger(encrypted, "access"), "Chave diferente não descriptografa token");
config.TokenEncryptionKey = oldKey;
var state = MelhorEnvioProtecao.GerarSegredo();
var hash = MelhorEnvioProtecao.Hash(state);
var now = DateTime.UtcNow;
Check(MelhorEnvioProtecao.StateValido(state, hash, now.AddMinutes(10), now), "State válido aceito");
Check(!MelhorEnvioProtecao.StateValido(state, hash, now, now), "State expirado rejeitado");
Check(!MelhorEnvioProtecao.StateValido(state, null, now.AddMinutes(10), now), "State consumido rejeitado");
Check(!MelhorEnvioProtecao.StateValido(MelhorEnvioProtecao.GerarSegredo(), hash, now.AddMinutes(10), now), "State de outra tentativa rejeitado");
Check(!MelhorEnvioProtecao.Confere(null, hash) && !MelhorEnvioProtecao.Confere("outro-navegador", hash), "Cookie ausente ou de outro navegador rejeitado");

var handler = new FakeHandler();
var client = new MelhorEnvioOAuthClient(new HttpClient(handler), options);
var url = new Uri(client.UrlAutorizacao(state));
var query = QueryHelpers.ParseQuery(url.Query);
Check(url.Host == "sandbox.melhorenvio.com.br" && query["state"] == state && query["scope"] == "shipping-calculate",
    "Autorização Sandbox solicita somente cotação e carrega state");
Check(!url.Query.Contains(config.ClientSecret) && query["redirect_uri"] == config.RedirectUri, "URL não expõe secret e preserva callback exato");
var tokens = await client.TrocarCodigoAsync("fake-code", CancellationToken.None);
Check(tokens.AccessToken == "access-test" && tokens.RefreshToken == "refresh-test" && tokens.ExpiresIn == 2592000, "Resposta OAuth interpretada");
using (var body = JsonDocument.Parse(handler.Body!))
{
    Check(body.RootElement.GetProperty("grant_type").GetString() == "authorization_code" &&
        body.RootElement.GetProperty("code").GetString() == "fake-code", "Troca de código envia grant correto");
}
Check(handler.Url == "https://sandbox.melhorenvio.com.br/oauth/token" && handler.UserAgent!.Contains(config.EmailContato), "Endpoint e contato técnico corretos");
await client.RenovarAsync("old-refresh-test", CancellationToken.None);
using (var body = JsonDocument.Parse(handler.Body!))
    Check(body.RootElement.GetProperty("grant_type").GetString() == "refresh_token" &&
        body.RootElement.GetProperty("refresh_token").GetString() == "old-refresh-test" &&
        !body.RootElement.TryGetProperty("code", out _), "Renovação envia refresh token e não reutiliza code");

async Task RejectResponse(HttpStatusCode status, string body, int expected, string name)
{
    handler.Status = status;
    handler.ResponseBody = body;
    try { await client.TrocarCodigoAsync("fake-code", CancellationToken.None); throw new Exception("Falha deveria ser propagada"); }
    catch (MelhorEnvioException ex)
    {
        Check(ex.HttpStatus == expected && !ex.Message.Contains("secret-test") && !ex.Message.Contains("fake-code"), name);
    }
}
await RejectResponse(HttpStatusCode.BadRequest, "{\"error\":\"invalid_grant\",\"details\":\"secret-test\"}", 409, "Revogação exige nova autorização sem vazar detalhes");
await RejectResponse(HttpStatusCode.InternalServerError, "secret-test", 502, "Erro externo não expõe corpo");
await RejectResponse(HttpStatusCode.OK, "{}", 502, "Resposta incompleta rejeitada");
await RejectResponse(HttpStatusCode.OK, "{\"access_token\":\"secret-test\",\"refresh_token\":\"r\",\"expires_in\":0,\"token_type\":\"Bearer\"}", 502, "Token sem validade rejeitado");

var controller = typeof(MelhorEnvioController);
Check(controller.GetCustomAttribute<AuthorizeAttribute>()?.Roles == "Admin", "Controller exige administrador");
foreach (var method in new[] { "Autorizar", "Status", "Renovar" })
    Check(controller.GetMethod(method)!.GetCustomAttribute<AllowAnonymousAttribute>() == null, method + " não libera acesso anônimo");
Check(controller.GetMethod("Callback")!.GetCustomAttribute<AllowAnonymousAttribute>() != null, "Callback aceita navegação do provedor");
Check(typeof(AuthController).GetMethod("ResetarSenhaAdmin") == null, "Reset público de senha fixa removido");
using var db = new PegaVisaoContext(new DbContextOptionsBuilder<PegaVisaoContext>()
    .UseNpgsql("Host=127.0.0.1;Database=unused;Username=unused;Password=unused").Options);
Check(db.Model.FindEntityType(typeof(PegaVisaoApi.Models.MelhorEnvioConexao))!.FindPrimaryKey()!.Properties.Single().Name == "Id",
    "Conexão possui chave única por ambiente/aplicativo");
Console.WriteLine($"{count} verificações passaram, sem conectar ao banco ou ao provedor.");

sealed class FakeHandler : HttpMessageHandler
{
    public HttpStatusCode Status = HttpStatusCode.OK;
    public string ResponseBody = "{\"access_token\":\"access-test\",\"refresh_token\":\"refresh-test\",\"expires_in\":2592000,\"token_type\":\"Bearer\"}";
    public string? Body, Url, UserAgent;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Url = request.RequestUri!.ToString();
        Body = await request.Content!.ReadAsStringAsync(cancellationToken);
        UserAgent = request.Headers.UserAgent.ToString();
        return new(Status) { Content = new StringContent(ResponseBody, Encoding.UTF8, "application/json") };
    }
}

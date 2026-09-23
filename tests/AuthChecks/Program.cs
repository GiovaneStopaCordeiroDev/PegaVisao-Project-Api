using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using PegaVisaoApi.Controllers;
using PegaVisaoApi.Data;
using PegaVisaoApi.Data.Dtos.AuthDtos;
using PegaVisaoApi.Models;
using PegaVisaoApi.Services;

int checks = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine("OK: " + name); checks++; }
var connection = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("AUTH_TEST_CONNECTION") ?? "");
if (connection.Host != "127.0.0.1" || connection.Database != "estoque_tests") throw new Exception("Exige banco local estoque_tests.");
await using var admin = new NpgsqlConnection(connection.ConnectionString);
await admin.OpenAsync();
var database = "auth_test_" + Guid.NewGuid().ToString("N");
await using (var command = new NpgsqlCommand($"CREATE DATABASE {database}", admin)) await command.ExecuteNonQueryAsync();
connection.Database = database;
var options = new DbContextOptionsBuilder<PegaVisaoContext>().UseNpgsql(connection.ConnectionString).Options;
PegaVisaoContext Db() => new(options);
var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
    ["Jwt:Key"] = new string('x', 64), ["Google:ClientId"] = "teste.apps.googleusercontent.com", ["Auth:FrontendUrl"] = "https://example.invalid"
}).Build();
AuthController Controller(PegaVisaoContext db) => new(db, config);
var mail = new FakeMail();
var logger = NullLogger<AuthController>.Instance;
const string email = "cliente@example.invalid";
const string senha = "senhaTeste123";
AuthController.EsqueciSenhaDto Forgot(string e = email) => new(e);
string Token() => mail.Link!.Split("&token=")[1];
try {
    await using (var db = Db()) {
        await db.Database.MigrateAsync();
        Check(!(await db.Database.GetPendingMigrationsAsync()).Any(), "Migration aplica em banco isolado");
        Check(await Controller(db).Registrar(new() { Nome="Cliente", Email=email, Senha=senha }) is CreatedResult, "Cadastro normal preservado");
    }
    async Task<IActionResult> Esqueci(string e = email) { await using var db = Db(); return await Controller(db).EsqueciSenha(Forgot(e), mail, logger, default); }
    async Task<IActionResult> Reset(string token, string novaSenha = "novaSenha123") { await using var db = Db(); return await Controller(db).RedefinirSenha(new(email, token, novaSenha), default); }
    var known = (OkObjectResult)await Esqueci();
    var unknown = (OkObjectResult)await Esqueci("inexistente@example.invalid");
    Check(JsonSerializer.Serialize(known.Value) == JsonSerializer.Serialize(unknown.Value) && mail.Count == 1, "Resposta genérica e sem envio para conta inexistente");
    var token = Token();
    await using (var db = Db()) {
        var user = await db.Usuarios.SingleAsync();
        Check(user.RecuperacaoHash == Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))) && user.RecuperacaoHash != token, "Banco armazena somente hash do segredo");
        Check(user.RecuperacaoExpiraEm > DateTime.UtcNow.AddMinutes(29), "Prazo de recuperação de 30 minutos");
    }
    await Esqueci(); Check(mail.Count == 1, "Cooldown impede reenvio imediato");
    Check(await Reset(new string('0',64)) is BadRequestObjectResult, "Token incorreto rejeitado");
    Check(await Reset(token, "curta") is BadRequestObjectResult, "Senha curta rejeitada");
    var simultaneous = await Task.WhenAll(Reset(token), Reset(token));
    Check(simultaneous.Count(r => r is OkObjectResult) == 1, "Consumo concorrente permite apenas uma redefinição");
    Check(await Reset(token) is BadRequestObjectResult, "Link usado não pode ser reutilizado");
    await using (var db = Db()) {
        Check(await Controller(db).Login(new() { Email=email, Senha=senha }) is UnauthorizedObjectResult, "Senha antiga deixa de funcionar");
        var result = (OkObjectResult)await Controller(db).Login(new() { Email=email, Senha="novaSenha123" });
        var session = (UsuarioLogadoDto)result.Value!;
        Check(new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(session.Token).Claims.Any(c => c.Type=="versao" && c.Value=="1"), "JWT carrega nova versão após recuperação");
        await db.Usuarios.ExecuteUpdateAsync(s => s.SetProperty(u => u.RecuperacaoEnviadaEm, DateTime.UtcNow.AddMinutes(-3)));
    }
    await Esqueci(); token = Token();
    await using (var db = Db()) await db.Usuarios.ExecuteUpdateAsync(s => s.SetProperty(u => u.RecuperacaoExpiraEm, DateTime.UtcNow.AddSeconds(-1)));
    Check(await Reset(token) is BadRequestObjectResult, "Token expirado rejeitado");
    await using (var db = Db()) await db.Usuarios.ExecuteUpdateAsync(s => s.SetProperty(u => u.RecuperacaoEnviadaEm, DateTime.UtcNow.AddMinutes(-3)));
    mail.Fail = true; await Esqueci(); mail.Fail = false;
    await using (var db = Db()) Check((await db.Usuarios.SingleAsync()).RecuperacaoHash == null, "Falha SMTP invalida link não enviado");
    mail.Configurado = false;
    Check(await Esqueci() is ObjectResult { StatusCode:503 }, "Envio desconfigurado retorna indisponibilidade");
    mail.Configurado = true;
    var google = new FakeGoogle { Email=email, Subject="google-1" };
    async Task<IActionResult> Google(string? password = null) { await using var db = Db(); return await Controller(db).GoogleLogin(new("fake-validado-pelo-double",password), google, default); }
    Check(await Google() is ConflictObjectResult, "Google não vincula automaticamente por e-mail");
    Check(await Google("errada") is UnauthorizedObjectResult, "Vínculo Google rejeita senha incorreta");
    Check(await Google("novaSenha123") is OkObjectResult, "Senha correta autoriza vínculo Google");
    Check(await Google() is OkObjectResult, "Conta vinculada entra sem senha pelo subject");
    await using (var db = Db()) Check(await db.Usuarios.CountAsync() == 1, "Vínculo mantém usuário existente");
    google.Email="novo@example.invalid"; google.Subject="google-2";
    var newGoogle = (OkObjectResult)await Google();
    Check(!((UsuarioLogadoDto)newGoogle.Value!).IsAdmin, "Novo usuário Google nunca recebe privilégio admin");
    await using (var db = Db()) Check(await Controller(db).Login(new() { Email=google.Email, Senha="qualquer" }) is UnauthorizedObjectResult, "Conta somente Google não aceita senha vazia ou arbitrária");
    google.Verified = false; Check(await Google() is UnauthorizedResult, "Google exige e-mail verificado");
    await using (var db = Db()) {
        Check(await Controller(db).GoogleLogin(new("token-forjado",null), new GoogleIdentidade(), default) is UnauthorizedObjectResult, "Validador real rejeita token Google forjado");
    }
    var emailConfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
        ["Email:ApiKey"]="teste-nao-real", ["Email:Remetente"]="loja@example.invalid", ["Auth:FrontendUrl"]="https://example.invalid"
    }).Build();
    var handler = new FakeHttp();
    var sender = new EmailRecuperacao(emailConfig, new HttpClient(handler));
    Check(sender.Configurado, "Resend configurado sem portas SMTP");
    await sender.Enviar(email, "https://example.invalid/redefinir-senha#token=teste", default);
    Check(handler.Valid, "Resend envia via HTTPS com payload e credencial no header");
    handler.Fail=true;
    try { await sender.Enviar(email, "https://example.invalid", default); throw new Exception("Aceitou falha HTTP"); }
    catch (HttpRequestException) { Check(true, "Falha do provedor HTTP é propagada"); }
    emailConfig["Auth:FrontendUrl"]="http://loja.example.invalid";
    Check(!sender.Configurado, "Links de recuperação exigem HTTPS fora de localhost");
    Console.WriteLine($"{checks} verificações aprovadas.");
} finally {
    NpgsqlConnection.ClearAllPools();
    await using var drop = new NpgsqlCommand($"DROP DATABASE {database} WITH (FORCE)", admin); await drop.ExecuteNonQueryAsync();
}
sealed class FakeMail : IEmailRecuperacao {
    public bool Configurado { get; set; } = true;
    public bool Fail { get; set; }
    public string? Link { get; set; }
    public int Count { get; set; }
    public Task Enviar(string email, string link, CancellationToken ct) { if (Fail) throw new InvalidOperationException(); Link=link; Count++; return Task.CompletedTask; }
}
sealed class FakeGoogle : IGoogleIdentidade {
    public string Email { get; set; } = ""; public string Subject { get; set; } = ""; public bool Verified { get; set; } = true;
    public Task<GoogleJsonWebSignature.Payload> Validar(string token, string clientId) => Task.FromResult(new GoogleJsonWebSignature.Payload { Email=Email, Subject=Subject, EmailVerified=Verified, Name="Cliente Google" });
}
sealed class FakeHttp : HttpMessageHandler {
    public bool Valid { get; set; }
    public bool Fail { get; set; }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) {
        var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
        Valid = request.RequestUri!.ToString() == "https://api.resend.com/emails" && request.Method == HttpMethod.Post &&
            request.Headers.Authorization?.Scheme == "Bearer" && json.RootElement.GetProperty("to")[0].GetString() == "cliente@example.invalid" &&
            json.RootElement.GetProperty("text").GetString()!.Contains("#token=teste");
        return new HttpResponseMessage(Fail ? System.Net.HttpStatusCode.BadRequest : System.Net.HttpStatusCode.OK);
    }
}
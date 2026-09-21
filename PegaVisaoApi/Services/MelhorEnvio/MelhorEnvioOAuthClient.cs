using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace PegaVisaoApi.Services.MelhorEnvio;

public sealed record MelhorEnvioTokens(string AccessToken, string RefreshToken, int ExpiresIn);

public sealed class MelhorEnvioOAuthClient(HttpClient http, IOptions<MelhorEnvioOptions> options)
{
    private readonly MelhorEnvioOptions _options = options.Value;

    public string UrlAutorizacao(string state)
    {
        _options.Validar();
        return QueryHelpers.AddQueryString(_options.BaseUrl + "/oauth/authorize", new Dictionary<string, string?>
        {
            ["client_id"] = _options.ClientId, ["redirect_uri"] = _options.RedirectUri,
            ["response_type"] = "code", ["scope"] = "shipping-calculate", ["state"] = state
        });
    }

    public Task<MelhorEnvioTokens> TrocarCodigoAsync(string code, CancellationToken ct) =>
        SolicitarAsync("authorization_code", "code", code, ct);
    public Task<MelhorEnvioTokens> RenovarAsync(string refreshToken, CancellationToken ct) =>
        SolicitarAsync("refresh_token", "refresh_token", refreshToken, ct);

    private async Task<MelhorEnvioTokens> SolicitarAsync(string grant, string campo, string valor, CancellationToken ct)
    {
        _options.Validar();
        using var request = new HttpRequestMessage(HttpMethod.Post, _options.BaseUrl + "/oauth/token");
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.UserAgent.ParseAdd($"PegaVisao/1.0 ({_options.EmailContato})");
        request.Content = JsonContent.Create(new Dictionary<string, string>
        {
            ["grant_type"] = grant, ["client_id"] = _options.ClientId,
            ["client_secret"] = _options.ClientSecret, ["redirect_uri"] = _options.RedirectUri,
            [campo] = valor
        });
        using var response = await http.SendAsync(request, ct);
        // Nunca incluir body do provedor, códigos ou tokens em exceções/logs.
        if (!response.IsSuccessStatusCode)
        {
            var invalidGrant = false;
            if (response.StatusCode == HttpStatusCode.BadRequest || response.StatusCode == HttpStatusCode.Unauthorized)
            {
                try
                {
                    using var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                    invalidGrant = error.RootElement.TryGetProperty("error", out var value) && value.GetString() == "invalid_grant";
                }
                catch (JsonException) { }
            }
            if (invalidGrant)
                throw new MelhorEnvioException(409, "Autorização expirada ou revogada. Conecte o Melhor Envio novamente.");
            throw new MelhorEnvioException(502, $"Melhor Envio recusou a solicitação OAuth (HTTP {(int)response.StatusCode}). Confira as credenciais e tente conectar novamente.");
        }
        try
        {
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var root = json.RootElement;
            var access = root.GetProperty("access_token").GetString();
            var refresh = root.GetProperty("refresh_token").GetString();
            var expires = root.GetProperty("expires_in").GetInt32();
            if (root.GetProperty("token_type").GetString()?.Equals("Bearer", StringComparison.OrdinalIgnoreCase) != true ||
                string.IsNullOrWhiteSpace(access) || string.IsNullOrWhiteSpace(refresh) || expires <= 0 || expires > 31536000)
                throw new JsonException();
            return new(access, refresh, expires);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new MelhorEnvioException(502, "Resposta OAuth inválida do Melhor Envio. Conecte novamente.");
        }
    }
}

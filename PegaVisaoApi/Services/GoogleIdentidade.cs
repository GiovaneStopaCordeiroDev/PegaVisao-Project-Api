using Google.Apis.Auth;
namespace PegaVisaoApi.Services;
public interface IGoogleIdentidade { Task<GoogleJsonWebSignature.Payload> Validar(string token, string clientId); }
public sealed class GoogleIdentidade : IGoogleIdentidade
{
    public Task<GoogleJsonWebSignature.Payload> Validar(string token, string clientId) =>
        GoogleJsonWebSignature.ValidateAsync(token, new GoogleJsonWebSignature.ValidationSettings { Audience = new[] { clientId } });
}
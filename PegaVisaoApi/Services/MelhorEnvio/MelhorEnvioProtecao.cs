using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace PegaVisaoApi.Services.MelhorEnvio;

public sealed class MelhorEnvioProtecao(IOptions<MelhorEnvioOptions> options)
{
    public static string GerarSegredo() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static bool StateValido(string? state, string? hash, DateTime? expiraEm, DateTime agora) =>
        state?.Length == 64 && expiraEm > agora && Confere(state, hash);
    public static bool Confere(string? value, string? hash) => !string.IsNullOrEmpty(value) && hash != null &&
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Hash(value)), Encoding.ASCII.GetBytes(hash));

    public string Proteger(string texto, string finalidade)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var plain = Encoding.UTF8.GetBytes(texto);
        var cipher = new byte[plain.Length];
        using var aes = new AesGcm(Convert.FromBase64String(options.Value.TokenEncryptionKey), 16);
        aes.Encrypt(nonce, plain, cipher, tag, Contexto(finalidade));
        CryptographicOperations.ZeroMemory(plain);
        return Convert.ToBase64String(nonce.Concat(tag).Concat(cipher).ToArray());
    }

    public string Desproteger(string texto, string finalidade)
    {
        var bytes = Convert.FromBase64String(texto);
        if (bytes.Length < 28) throw new CryptographicException("Token protegido inválido.");
        var plain = new byte[bytes.Length - 28];
        using var aes = new AesGcm(Convert.FromBase64String(options.Value.TokenEncryptionKey), 16);
        aes.Decrypt(bytes.AsSpan(0, 12), bytes.AsSpan(28), bytes.AsSpan(12, 16), plain, Contexto(finalidade));
        var result = Encoding.UTF8.GetString(plain);
        CryptographicOperations.ZeroMemory(plain);
        return result;
    }

    private byte[] Contexto(string finalidade) => Encoding.UTF8.GetBytes($"PegaVisao:MelhorEnvio:v1:{options.Value.ConexaoId}:{finalidade}");
}

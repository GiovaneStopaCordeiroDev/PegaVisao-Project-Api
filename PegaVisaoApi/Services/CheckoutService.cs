using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PegaVisaoApi.Data;
using PegaVisaoApi.DTO_s;
using PegaVisaoApi.Models;
using PegaVisaoApi.Services.Frete;
namespace PegaVisaoApi.Services;

public sealed class CheckoutService(PegaVisaoContext db, FreteService frete)
{
    public static string Hash(CreatePedidoDto dto) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {
        itens = dto.Itens.GroupBy(i => i.VariacaoProdutoId).OrderBy(g => g.Key).Select(g => new { id = g.Key, quantidade = g.Sum(i => i.Quantidade) }),
        cep = FreteRegras.NormalizarCep(dto.Cep), dto.Rua, dto.Numero, dto.Complemento, dto.Bairro, dto.Cidade, dto.Estado,
        dto.FormaPagamento, dto.FreteServicoId, cupom = CupomService.Normalizar(dto.CupomCodigo)
    }))));

    public async Task<Pedido> CriarOuObterAsync(int usuarioId, CreatePedidoDto dto, CancellationToken ct)
    {
        if (dto.Itens == null || dto.Itens.Count == 0 || dto.Itens.Any(i => i.Quantidade <= 0)) throw new FreteException(400, "Informe itens válidos.");
        if (dto.FormaPagamento is not ("Pix" or "Cartão")) throw new FreteException(400, "Forma de pagamento inválida.");
        var sessao = dto.CheckoutSessionId ?? dto.CotacaoFreteId;
        if (sessao == null || sessao == Guid.Empty) throw new FreteException(400, "Informe uma sessão de checkout válida.");
        var hash = Hash(dto);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Lock por sessão entre instâncias. A constraint UNIQUE permanece como garantia final.
        var lockId = BitConverter.ToInt64(SHA256.HashData(sessao.Value.ToByteArray()), 0);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({lockId})", ct);
        var existente = await db.Pedidos.SingleOrDefaultAsync(p => p.CheckoutSessionId == sessao, ct);
        if (existente != null) return Conferir(existente, usuarioId, hash);
        // Compatibilidade com tentativas antigas que já consumiram a cotação.
        existente = await db.Pedidos.SingleOrDefaultAsync(p => p.FreteCotacaoId == dto.CotacaoFreteId, ct);
        if (existente != null) {
            if (existente.UsuarioId != usuarioId) throw new FreteException(409, "Cotação já utilizada.");
            return existente;
        }
        var pedido = new Pedido {
            UsuarioId = usuarioId, CheckoutSessionId = sessao, CheckoutHash = hash,
            PaymentIdempotencyKey = Guid.NewGuid().ToString(), PagamentoExpiraEm = DateTime.UtcNow.AddMinutes(15),
            Cep = dto.Cep, Rua = dto.Rua, Numero = dto.Numero, Complemento = dto.Complemento,
            Bairro = dto.Bairro, Cidade = dto.Cidade, Estado = dto.Estado,
            FormaPagamento = dto.FormaPagamento, CupomCodigo = string.IsNullOrWhiteSpace(dto.CupomCodigo) ? null : CupomService.Normalizar(dto.CupomCodigo)
        };
        try {
            await frete.SalvarPedidoAsync(pedido, dto.CotacaoFreteId ?? Guid.Empty, dto.FreteServicoId, dto.Itens, ct);
            await tx.CommitAsync(ct);
            return pedido;
        } catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }) {
            await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            existente = await db.Pedidos.SingleOrDefaultAsync(p => p.CheckoutSessionId == sessao, ct);
            if (existente != null) return Conferir(existente, usuarioId, hash);
            throw new FreteException(409, "Esta cotação já possui um pedido. Recupere a tentativa existente.");
        }
    }
    private static Pedido Conferir(Pedido pedido, int usuarioId, string hash)
    {
        if (pedido.UsuarioId != usuarioId || pedido.CheckoutHash != hash)
            throw new FreteException(409, "A sessão já está vinculada a outro checkout. Inicie uma nova tentativa para alterar os itens.");
        return pedido; // Pedidos encerrados são retornados; nunca se cria outra cobrança automaticamente.
    }
}
